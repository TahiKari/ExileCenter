using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace CorpseCleaner;

internal sealed record PermanentVisualOptions
{
    public bool HideCorpses { get; init; } = true;
    public float? CameraDistance { get; init; }
    public bool MapBorders { get; init; }
    public float[] BorderColor { get; init; } = [0.5f, 0.5f, 1f, 0.5f];
    public float[] FogColor { get; init; } = [0f, 0f, 0f, 0f];

    internal void Validate()
    {
        if (!HideCorpses && CameraDistance == null && !MapBorders)
            throw new InvalidDataException("Choisissez au moins un réglage permanent.");
        if (CameraDistance is float distance && (!float.IsFinite(distance) || distance < 0.5f || distance > 3f))
            throw new InvalidDataException("La distance permanente doit être comprise entre 50 % et 300 %.");
        foreach (var color in new[] { BorderColor, FogColor })
            if (color == null || color.Length != 4 || color.Any(v => !float.IsFinite(v) || v < 0f || v > 1f))
                throw new InvalidDataException("Couleur ou opacité invalide.");
    }
}

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
        if (root && blocks.Length > 1) throw new InvalidDataException("Plusieurs blocs Life dans le script de base : modification annulée.");
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
        var edits = new List<(int Position, string Text)>();
        // Official child scripts may repeat the component; preserve every block and its events.
        foreach (var block in blocks)
        {
            int open = block.Index + block.Length - 1, close = Closing(mask, open);
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
        }
        if (edits.Count == 0) return enabling.Length == 0 ? null : Utf16.GetBytes(text);
        foreach (var edit in edits.OrderByDescending(e => e.Position)) text = text.Insert(edit.Position, edit.Text);
        _ = Mask(text); // Validate balanced delimiters after the edit.
        return Utf16.GetBytes(text);
    }

    internal static byte[] PatchCamera(byte[] original, float distance)
    {
        new PermanentVisualOptions { CameraDistance = distance }.Validate();
        if (original.Length == 0 || original.Length > 512 * 1024 || original.Length % 2 != 0)
            throw new InvalidDataException("Format du script de caméra non pris en charge.");
        string text = Utf16.GetString(original), mask = Mask(text);
        if (text.Contains("ExileCenter persistent camera", StringComparison.Ordinal) ||
            Regex.IsMatch(mask, @"\bCreateCameraZoomNode\s*\("))
            throw new InvalidDataException("Une caméra personnalisée est déjà présente. Restaurez-la avant d'appliquer ce réglage.");
        var blocks = Regex.Matches(mask, @"\bPositioned\s*\{").Where(m => Depth(mask, m.Index) == 0).ToArray();
        if (blocks.Length != 1) throw new InvalidDataException("Bloc Positioned de la caméra absent ou ambigu.");
        int open = blocks[0].Index + blocks[0].Length - 1, close = Closing(mask, open);
        var events = Regex.Matches(mask, @"\bon_initial_position_set\s*=\s*\{")
            .Where(m => m.Index > open && m.Index < close && Depth(mask, m.Index) == 1).ToArray();
        if (events.Length > 1) throw new InvalidDataException("Événement de caméra ambigu.");
        string script = " CreateCameraZoomNode( 5000.0, 5000.0, " + distance.ToString("0.######", CultureInfo.InvariantCulture) + " ); ";
        int at = events.Length == 1 ? Closing(mask, events[0].Index + events[0].Length - 1) : close;
        string insertion = events.Length == 1 ? script : "\r\n\ton_initial_position_set = {" + script + "}\r\n";
        text = text.Insert(at, insertion);
        _ = Mask(text);
        return Utf16.GetBytes(text + "\r\n// ExileCenter persistent camera\r\n");
    }

    internal static byte[] PatchMinimap(byte[] original, PermanentVisualOptions options)
    {
        options.Validate();
        if (original.Length == 0 || original.Length > 512 * 1024) throw new InvalidDataException("Format du shader de carte non pris en charge.");
        string text = new UTF8Encoding(false, true).GetString(original);
        if (text.Contains("ExileCenter persistent minimap", StringComparison.Ordinal))
            throw new InvalidDataException("La carte est déjà personnalisée. Restaurez-la avant de changer ses réglages.");
        string Number(float value) => value.ToString("0.0#####", CultureInfo.InvariantCulture) + "f";
        string Color(float[] color) => "float4(" + string.Join(", ", color.Select(Number)) + ")";
        string ReplaceOnce(string input, string before, string after)
        {
            int at = input.IndexOf(before, StringComparison.Ordinal);
            if (at < 0 || input.IndexOf(before, at + before.Length, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException("Shader de carte incompatible : aucun fichier du jeu n'a été remplacé.");
            return input.Remove(at, before.Length).Insert(at, after);
        }
        text = ReplaceOnce(text,
            "float4 walkability_map_color = lerp(walkable_color, float4(0.5f, 0.5f, 1.0f, 0.5f), walkable_to_edge_ratio);",
            "float4 walkability_map_color = lerp(walkable_color, " + Color(options.BorderColor) + ", walkable_to_edge_ratio);");
        text = ReplaceOnce(text,
            "float4(0.0f, 0.5f, 1.0f, 0.5f), pow(saturate(unexplored_edge), 25.0f)",
            Color(options.BorderColor) + ", pow(saturate(unexplored_edge), 25.0f)");
        const string anchor = "float max_walkable_fade = lerp(1.0f, 0.3f, walkability_opacity);";
        string rendering = "// ExileCenter persistent minimap: exploration visibility is unchanged.\r\n\t" +
            "walkability_map_color.a = max(walkability_map_color.a, " + Number(options.BorderColor[3]) + " * walkable_to_edge_ratio * (1.0f - edge_to_unwalkable_ratio));\r\n\t";
        if (options.FogColor[3] > 0f)
            rendering += "float4 ec_fog = " + Color(options.FogColor) + ";\r\n\tec_fog.a *= (1.0f - visibility) * (1.0f - edge_to_unwalkable_ratio);\r\n\twalkability_map_color = Uberblend(ec_fog, walkability_map_color);\r\n\t";
        text = ReplaceOnce(text, anchor, rendering + anchor);
        return Encoding.UTF8.GetBytes(text);
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
