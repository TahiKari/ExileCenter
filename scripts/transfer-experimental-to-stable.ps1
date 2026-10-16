# Uebertraegt Aenderungen vom Experimental-Quellprojekt nach Stabil (nur auf explizite Anweisung).
param(
    [string]$ExperimentalRoot,
    [string]$StableRoot,
    [string[]]$Paths,
    [switch]$All,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "project-paths.ps1")

if ([string]::IsNullOrWhiteSpace($ExperimentalRoot)) { $ExperimentalRoot = $GameHelperExperimentalSrcRoot }
if ([string]::IsNullOrWhiteSpace($StableRoot)) { $StableRoot = $GameHelperStableRoot }

if (-not (Test-Path $ExperimentalRoot)) {
    throw "Experimental-Quellcode fehlt: $ExperimentalRoot"
}
if (-not (Test-Path $StableRoot)) {
    throw "Stabiler Quellcode fehlt: $StableRoot"
}

$defaultPaths = @(
    "Plugins",
    "GameHelper",
    "Launcher",
    "Downloader",
    "GameOffsets",
    "Shared",
    "scripts\build.ps1",
    "scripts\build-downloader.ps1",
    "scripts\publish.ps1",
    "scripts\set-version.ps1",
    "ExileCenter.sln"
)

$transferPaths = if ($Paths -and $Paths.Count -gt 0) {
    $Paths
}
elseif ($All) {
    $defaultPaths
}
else {
    throw "Bitte -Paths angeben (z.B. Plugins/Radar) oder -All fuer den Standard-Satz."
}

Write-Host ""
Write-Host "=== Experimental -> Stabil ===" -ForegroundColor Cyan
Write-Host "  Von: $ExperimentalRoot" -ForegroundColor DarkGray
Write-Host "  Nach: $StableRoot" -ForegroundColor DarkGray
Write-Host ""

foreach ($rel in $transferPaths) {
    $relNorm = $rel.Replace('/', '\')
    $src = Join-Path $ExperimentalRoot $relNorm
    $dst = Join-Path $StableRoot $relNorm

    if (-not (Test-Path $src)) {
        Write-Host "  Ueberspringe (fehlt in Experimental): $relNorm" -ForegroundColor Yellow
        continue
    }

    if ($WhatIf) {
        Write-Host "  [WhatIf] $relNorm" -ForegroundColor DarkYellow
        continue
    }

    if (Test-Path $src -PathType Container) {
        if (-not (Test-Path $dst)) {
            New-Item -ItemType Directory -Path $dst -Force | Out-Null
        }
        robocopy $src $dst /E /XD bin obj /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw "robocopy fehlgeschlagen fuer $relNorm (Exit $LASTEXITCODE)"
        }
    }
    else {
        $dir = Split-Path $dst -Parent
        if ($dir -and -not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        Copy-Item $src $dst -Force
    }

    Write-Host "  OK $relNorm" -ForegroundColor Green
}

if (-not $WhatIf) {
    Write-Host ""
    Write-Host "Fertig. Stabil lokal testen (rebuild-test.bat), dann rebuild-and-publish.bat wenn gewuenscht." -ForegroundColor Green
    Write-Host "Experimental wurde NICHT geaendert." -ForegroundColor DarkGray
}
