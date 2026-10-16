namespace Launcher
{
    using System;
    using System.IO;
    public static class GameHelperFinder
    {
        private const string AppExeName = "GameHelper.exe";

        public static bool TryFindGameHelperExe(out string installDir, out string appExePath)
        {
            installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            appExePath = Path.Join(installDir, AppExeName);

            if (File.Exists(appExePath))
            {
                return true;
            }

            // Fallback: alte Benennung
            var legacyPath = Path.Join(installDir, "GameHelper.exe");
            if (File.Exists(legacyPath) && !string.Equals(
                Path.GetFileName(Environment.ProcessPath ?? "GameHelper.exe"),
                "GameHelper.exe",
                StringComparison.OrdinalIgnoreCase))
            {
                appExePath = legacyPath;
                return true;
            }

            Console.WriteLine($"{AppExeName} nicht gefunden in {installDir}.");
            Console.Write("Pfad zu GameHelper.App.exe angeben:");
            var path = (Console.ReadLine() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
            {
                installDir = path;
                appExePath = Path.Join(installDir, AppExeName);
            }
            else
            {
                installDir = Path.GetDirectoryName(path) ?? string.Empty;
                appExePath = path;
            }

            return File.Exists(appExePath);
        }
    }
}
