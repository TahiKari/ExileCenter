# Erstellt ExileCenterDownloader.exe (einzelne Datei, laedt oeffentlich von GitHub Releases).

param(

    [ValidateSet("Debug", "Release")]

    [string]$Configuration = "Release",

    [switch]$SelfContained

)



$ErrorActionPreference = "Stop"

$Root = Split-Path $PSScriptRoot -Parent

$Project = Join-Path $Root "Downloader\Downloader.csproj"

$OutDir = Join-Path $Root "publish-downloader"



Write-Host "=== ExileCenterDownloader bauen ($Configuration) ===" -ForegroundColor Cyan

Write-Host "Download-Quelle: GitHub Releases (oeffentlich, kein Token)." -ForegroundColor DarkGray



$publishArgs = @(

    "publish", $Project,

    "-c", $Configuration,

    "-r", "win-x64",

    "-o", $OutDir,

    "-p:PublishSingleFile=true"

)



if ($SelfContained) {

    $publishArgs += @(

        "-p:SelfContained=true",

        "-p:IncludeNativeLibrariesForSelfExtract=true",

        "-p:EnableCompressionInSingleFile=true"

    )

    Write-Host "Modus: self-contained (~50 MB, keine extra .NET-Installation)" -ForegroundColor DarkGray

}

else {

    $publishArgs += "-p:SelfContained=false"

    Write-Host "Modus: framework-dependent (~1 MB, .NET 10 Desktop Runtime noetig)" -ForegroundColor DarkGray

}



dotnet @publishArgs

if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen" }



$builtExe = Join-Path $OutDir "ExileCenterDownloader.exe"

$rootExe = Join-Path $Root "ExileCenterDownloader.exe"

Copy-Item $builtExe $rootExe -Force



$sizeKb = [math]::Round((Get-Item $rootExe).Length / 1KB)

Write-Host ""

Write-Host "Fertig ($sizeKb KB):" -ForegroundColor Green

Write-Host "  $rootExe"

Write-Host ""

Write-Host "Verteilen: nur diese eine EXE weitergeben." -ForegroundColor Cyan

if (-not $SelfContained) {

    Write-Host "Hinweis: Nutzer brauchen .NET 10 Desktop Runtime." -ForegroundColor DarkGray

}

Write-Host "Groessere Offline-EXE: build-downloader.bat -SelfContained" -ForegroundColor DarkGray


