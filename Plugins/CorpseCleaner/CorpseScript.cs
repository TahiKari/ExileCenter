using System.Text;
using System.Text.RegularExpressions;

namespace CorpseCleaner;

internal static class CorpseScript
{
    internal const string Marker = "gh_corpse_hidden";
    private static readonly UnicodeEncoding Utf16 = new(false, false, true);
    private static readonly Regex Life = new(@"\bLife\s*\{", RegexOptions.CultureInvariant);
    private static readonly Regex Enable = new(@"(?<![\w.])EnableRendering\s*\(\s*\)\s*;", RegexOptions.CultureInvariant);
    private static readonly (string Name, string Script)[] Events =
    {
        ("on_death", "this.gh_corpse_hidden = true; Delay( 1.0, { If( this.gh_corpse_hidden == true, () { DisableRendering(); } ); } );"),
        ("on_spawned_dead", "this.gh_corpse_hidden = true; DisableRendering();"),
        ("on_spawned_alive", "this.gh_corpse_hidden = false;"),
        ("on_revive", "this.gh_corpse_hidden = false; EnableRendering();")
    };

    internal static byte[]? Patch(byte[] original, bool root)
    {
        if (original.Length == 0 || original.Length > 512 * 1024 || original.Length % 2 != 0)
            throw new InvalidDataException("Format de script de monstre non pris en charge.");
        string text = Utf16.GetString(original);
        if (text.Contains(Marker, StringComparison.Ordinal)) return null;
        if (text.Contains("ef_corpse_dead", StringComparison.Ordinal))
            throw new InvalidDataException("Un patch de cadavres Exile Forge est déjà présent. Restaurez cette option dans Exile Forge avant d'utiliser CorpseCleaner.");
        string mask = Mask(text);
        var blocks = Life.Matches(mask).Where(m => Depth(mask, m.Index) == 0).ToArray();
        if (blocks.Length > 1) throw new InvalidDataException("Plusieurs blocs Life : modification annulée.");
        if (root && blocks.Length == 0) throw new InvalidDataException("Le bloc Life du script de base est absent.");
        // Preserve existing enable calls for living/uninitialized entities, while
        // preventing later animation/state callbacks from re-enabling a corpse.
        var enabling = Enable.Matches(mask).ToArray();
        foreach (var match in enabling.Reverse())
            text = text.Insert(match.Index + match.Length,
                " IfExists( this.gh_corpse_hidden, { If( this.gh_corpse_hidden == true, () { DisableRendering(); } ); } );");
        mask = Mask(text);
        blocks = Life.Matches(mask).Where(m => Depth(mask, m.Index) == 0).ToArray();
        if (blocks.Length == 0)
        {
            return enabling.Length == 0 ? null : Utf16.GetBytes(text);
        }
        int open = blocks[0].Index + blocks[0].Length - 1, close = Closing(mask, open);
        var edits = new List<(int Position, string Text)>();
        foreach (var item in Events)
        {
            var pattern = new Regex(@"\b" + item.Name + @"\s*=\s*\{", RegexOptions.CultureInvariant);
            var matches = pattern.Matches(mask, open + 1).Where(m => m.Index < close && Depth(mask, m.Index) == 1).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("Événement Life dupliqué : modification annulée.");
            if (matches.Length == 1)
            {
                int end = Closing(mask, matches[0].Index + matches[0].Length - 1);
                if (end > close) throw new InvalidDataException("Événement Life mal formé.");
                int begin = matches[0].Index + matches[0].Length;
                edits.Add((item.Name == "on_spawned_dead" ? end : begin, " " + item.Script + " "));
            }
            else if (root)
                edits.Add((close, "\r\n\t" + item.Name + " = { " + item.Script + " }\r\n"));
        }
        if (edits.Count == 0) return enabling.Length == 0 ? null : Utf16.GetBytes(text);
        foreach (var edit in edits.OrderByDescending(e => e.Position)) text = text.Insert(edit.Position, edit.Text);
        _ = Mask(text); // Validate balanced delimiters after the edit.
        return Utf16.GetBytes(text);
    }

    // Ignore quoted strings and comments when identifying script delimiters.
    internal static string Mask(string text)
    {
        char[] mask = text.ToCharArray();
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '"' or '\'')
            {
                char quote = text[i]; mask[i] = ' ';
                bool closed = false;
                while (++i < text.Length)
                {
                    char value = text[i]; mask[i] = value is '\r' or '\n' ? value : ' ';
                    if (value == '\\' && i + 1 < text.Length) { mask[++i] = ' '; continue; }
                    if (value == quote) { closed = true; break; }
                }
                if (!closed) throw new InvalidDataException("Chaîne de script non terminée.");
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') mask[i++] = ' ';
                i--;
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                mask[i++] = ' '; mask[i] = ' '; bool closed = false;
                while (++i < text.Length)
                {
                    if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/') { mask[i] = mask[++i] = ' '; closed = true; break; }
                    if (text[i] is not ('\r' or '\n')) mask[i] = ' ';
                }
                if (!closed) throw new InvalidDataException("Commentaire de script non terminé.");
            }
            else if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth < 0) throw new InvalidDataException("Accolades de script invalides.");
        }
        if (depth != 0) throw new InvalidDataException("Accolades de script non équilibrées.");
        return new string(mask);
    }

    private static int Depth(string mask, int end)
    {
        int depth = 0;
        for (int i = 0; i < end; i++) { if (mask[i] == '{') depth++; else if (mask[i] == '}') depth--; }
        return depth;
    }

    private static int Closing(string mask, int open)
    {
        int depth = 0;
        for (int i = open; i < mask.Length; i++)
        {
            if (mask[i] == '{') depth++;
            else if (mask[i] == '}' && --depth == 0) return i;
        }
        throw new InvalidDataException("Bloc de script non terminé.");
    }
}
