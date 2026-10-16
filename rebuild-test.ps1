# ExileCenter: neu bauen und in den Test-Ordner deployen (ohne GitHub-Upload).
# Doppelklick auf rebuild-test.bat oder:
#   powershell -ExecutionPolicy Bypass -File rebuild-test.ps1
#
# Optional:
#   -Configuration Debug          # zeigt CMD-Fenster mit Log-Ausgabe (nur fuer Entwicklung)
#   -OutputDir MeinTestordner

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputDir = "Test"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$BuildScript = Join-Path $Root "scripts\build.ps1"

if (-not (Test-Path $BuildScript)) {
    Write-Error "build.ps1 nicht gefunden. Bitte im Gamehelper-Projektordner starten."
}

$deployDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
}
else {
    Join-Path $Root $OutputDir
}

$started = Get-Date
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " ExileCenter Rebuild (Test)" -ForegroundColor Cyan
Write-Host " $(Get-Date -Format 'dd.MM.yyyy HH:mm:ss')" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Modus: Nur Build -> $deployDir" -ForegroundColor Yellow
Write-Host "Konfiguration: $Configuration" -ForegroundColor Yellow
Write-Host ""

& $BuildScript -Configuration $Configuration -OutputDir $OutputDir

if (-not (Test-Path (Join-Path $deployDir "ExileCenter.exe"))) {
    throw "ExileCenter.exe fehlt in $deployDir. Build unvollstaendig."
}

$elapsed = (Get-Date) - $started
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Fertig in $([math]::Round($elapsed.TotalSeconds, 1))s" -ForegroundColor Green
Write-Host " Testordner: $deployDir" -ForegroundColor Green
Write-Host " Starten:    $deployDir\ExileCenter.exe" -ForegroundColor Green
Write-Host " Version:    $deployDir\VERSION.txt" -ForegroundColor Green
Write-Host ""
Write-Host " Hinweis: publish\ bleibt unveraendert." -ForegroundColor DarkGray
Write-Host "         Test-Einstellungen werden vor dem Build gesichert und danach wiederhergestellt." -ForegroundColor DarkGray
if ($Configuration -eq "Debug") {
    Write-Host "         Debug-Build: CMD-Fenster mit Logs ist normal." -ForegroundColor DarkYellow
}
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
