# Einmaliges Setup: Quellcode aus Backup kopieren und Runtime-Dateien archivieren.
# Ausfuehren: powershell -ExecutionPolicy Bypass -File setup-project.ps1

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Backup = "D:\ZusatzProgramme\Sicherung\Download\ExileCenter2_v2.3.0_source"

if (-not (Test-Path $Backup)) {
    Write-Error "Backup nicht gefunden: $Backup"
}

Write-Host "=== Gamehelper Projekt-Setup ===" -ForegroundColor Cyan

# Runtime-Dateien (alte Deployment-Struktur) nach runtime-backup verschieben
$runtimeBackup = Join-Path $Root "runtime-backup"
if (-not (Test-Path $runtimeBackup)) {
    New-Item -ItemType Directory -Path $runtimeBackup | Out-Null
    $runtimePatterns = @("*.exe", "*.dll", "*.json", "*.ini", "configs", "imgui.ini")
    foreach ($pattern in $runtimePatterns) {
        Get-ChildItem -Path $Root -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.Name -ne "setup-project.ps1") {
                Move-Item $_.FullName $runtimeBackup -Force -ErrorAction SilentlyContinue
            }
        }
    }
    if (Test-Path (Join-Path $Root "configs")) {
        Move-Item (Join-Path $Root "configs") $runtimeBackup -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Runtime-Dateien nach runtime-backup/ verschoben." -ForegroundColor Yellow
}

function Copy-SourceTree {
    param([string]$RelativePath)
    $src = Join-Path $Backup $RelativePath
    $dst = Join-Path $Root $RelativePath
    if (-not (Test-Path $src)) {
        Write-Warning "Ueberspringe (nicht gefunden): $RelativePath"
        return
    }
    $markerFile = Join-Path $dst "*.csproj"
    if ((Test-Path $dst) -and (Get-ChildItem $markerFile -ErrorAction SilentlyContinue)) {
        Write-Host "Bereits vorhanden: $RelativePath" -ForegroundColor DarkGray
        return
    }
    robocopy $src $dst /E /XD bin obj .vs /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Error "robocopy fehlgeschlagen: $RelativePath" }
    Write-Host "Kopiert: $RelativePath" -ForegroundColor Green
}

# Kern-Projekte
@(
    "ExileCenter",
    "GameOffsets",
    "Launcher",
    "Plugins\AutoHotKeyTrigger",
    "Plugins\HealthBars",
    "Plugins\PreloadAlert",
    "Plugins\Radar"
) | ForEach-Object { Copy-SourceTree $_ }

# Shared config
foreach ($file in @("Directory.Build.props", "NuGet.config", ".editorconfig")) {
    $src = Join-Path $Backup $file
    $dst = Join-Path $Root $file
    if ((Test-Path $src) -and -not (Test-Path $dst)) {
        Copy-Item $src $dst
        Write-Host "Kopiert: $file" -ForegroundColor Green
    }
}

# ExileCenter als interner Overlay-Name
$ghProj = Join-Path $Root "ExileCenter\ExileCenter.csproj"
if (Test-Path $ghProj) {
    $xml = Get-Content $ghProj -Raw
    if ($xml -notmatch '<AssemblyName>ExileCenter</AssemblyName>') {
        $xml = $xml -replace '<TargetFramework>net10.0-windows</TargetFramework>', @'
<TargetFramework>net10.0-windows</TargetFramework>
    <AssemblyName>ExileCenter</AssemblyName>
    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
'@
        $xml = $xml -replace '(?s)<Target Name="CopyDocumentationFiles".*?</Target>\s*', ''
        Set-Content $ghProj $xml -Encoding UTF8
        Write-Host "ExileCenter.csproj angepasst (ExileCenter)" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Setup abgeschlossen. Naechste Schritte:" -ForegroundColor Cyan
Write-Host "  1. rebuild-test.bat                     # Build nach Test\ (lokales Testen)"
Write-Host "  2. scripts\build.ps1                    # Projekt bauen (nach publish\)"
Write-Host "  3. scripts\ensure-update-signing-key.ps1  # Update-Signatur (einmalig)"
Write-Host "  4. rebuild-and-publish.bat              # Build + GitHub Release"
