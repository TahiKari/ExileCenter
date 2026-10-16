namespace ExileCenter.Migration;

using System;
using System.Diagnostics;
using System.IO;
using ExileCenter.Utils;

// Consumed only by installers that still require the former executable names.
internal static class Program
{
    private static string Target(string relative)
    {
        string root = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(relative);
        for (string? parent = path; parent != null && parent.Length >= root.Length - 1; parent = Path.GetDirectoryName(parent))
            if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0 &&
                !UpdatePathHelper.IsAllowedReparsePoint(parent)) throw new InvalidDataException("Reparse point: " + parent);
        return path;
    }

    private static int Main(string[] args)
    {
        try
        {
            foreach (string file in new[] { "ExileCenter.exe", "ExileCenter.deps.json", "ExileCenter.runtimeconfig.json" })
            {
                string source = Target("Localization/ExileCenterMigration/" + (file == "ExileCenter.exe" ? "ExileCenter.apphost" : file));
                string destination = Target(file);
                string temporary = Target(file + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try { File.Copy(source, temporary); File.Move(temporary, destination, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            if (!File.Exists(Target("ExileCenter.dll"))) throw new InvalidDataException("ExileCenter.dll missing.");
            bool versionOnly = args.Length == 1 && args[0] == "--version";
            var start = new ProcessStartInfo(versionOnly ? "dotnet.exe" : Target("ExileCenter.exe"))
            {
                WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = !versionOnly,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            };
            if (versionOnly) start.ArgumentList.Add(Target("ExileCenter.dll"));
            start.ArgumentList.Add("--complete-migration");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (string argument in args) start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new IOException("Unable to start ExileCenter.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try { File.WriteAllText(Target("local-update-error.txt"), error.ToString()); } catch { }
            return 1;
        }
    }
}
