namespace Launcher
{
    using System;
    using System.Diagnostics;
    using System.Windows.Forms;

    internal static class GameStarter
    {
        internal static bool TryStart(string installDir, string appExePath)
        {
            try
            {
                var newName = MiscHelper.GenerateRandomString();
                TemporaryFileManager.Purge();

                var gameHelperPath = ExileCenterTransformer.TransformExileCenterExecutable(installDir, appExePath, newName);
                Process.Start(new ProcessStartInfo
                {
                    FileName = gameHelperPath,
                    WorkingDirectory = installDir,
                    UseShellExecute = true,
                });
                LauncherLog.Write($"Overlay gestartet: {gameHelperPath}");
                return true;
            }
            catch (Exception ex)
            {
                LauncherLog.Write($"Fehler: {ex}");
                var incompleteHint = ex.Message.Contains("AsmResolver", StringComparison.OrdinalIgnoreCase)
                    ? Environment.NewLine + Environment.NewLine + LauncherLocalization.L(
                        "Installation incomplete. Delete the folder and run GameHelperDownloader again into an empty folder.",
                        "Installation unvollstaendig. Ordner loeschen und GameHelperDownloader erneut in einen LEEREN Ordner ausfuehren.")
                    : string.Empty;
                LauncherDialogs.ShowError(
                    LauncherLocalization.L(
                        $"Start failed:{Environment.NewLine}{ex.Message}{incompleteHint}",
                        $"Start fehlgeschlagen:{Environment.NewLine}{ex.Message}{incompleteHint}"));
                return false;
            }
        }
    }
}
