# Baut die komplette Solution und kopiert das Ergebnis in den Zielordner (Standard: publish/)
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$Solution = Join-Path $Root "GameOverlay.sln"
$PublishDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
}
else {
    Join-Path $Root $OutputDir
}
. (Join-Path $PSScriptRoot "set-version.ps1")

function Invoke-Robocopy {
    param([string[]]$Arguments)

    & robocopy @Arguments | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy fehlgeschlagen (Exit-Code $LASTEXITCODE)"
    }
}

function Save-DeployUserData {
    param(
        [string]$SourceDir,
        [string]$BackupDir
    )

    if (-not (Test-Path $SourceDir)) {
        return
    }

    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null

    $configs = Join-Path $SourceDir "configs"
    if (Test-Path $configs) {
        Invoke-Robocopy $configs, (Join-Path $BackupDir "configs"), "/E", "/NFL", "/NDL", "/NJH", "/NJS"
    }

    foreach ($extra in @("imgui.ini", "migration-notice.dismissed")) {
        $src = Join-Path $SourceDir $extra
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $BackupDir $extra) -Force
        }
    }

    $pluginsRoot = Join-Path $SourceDir "Plugins"
    if (-not (Test-Path $pluginsRoot)) {
        return
    }

    foreach ($plugin in Get-ChildItem $pluginsRoot -Directory) {
        $srcConfig = Join-Path $plugin.FullName "config"
        if (-not (Test-Path $srcConfig)) {
            continue
        }

        $dstConfig = Join-Path $BackupDir "Plugins\$($plugin.Name)\config"
        if (-not (Test-Path $dstConfig)) {
            New-Item -ItemType Directory -Path $dstConfig -Force | Out-Null
        }

        Invoke-Robocopy $srcConfig, $dstConfig, "/E", "/NFL", "/NDL", "/NJH", "/NJS"
    }
}

function Restore-DeployUserData {
    param(
        [string]$TargetDir,
        [string]$BackupDir
    )

    if (-not (Test-Path $BackupDir)) {
        return $false
    }

    $restored = $false
    $configs = Join-Path $BackupDir "configs"
    if (Test-Path $configs) {
        $dstConfigs = Join-Path $TargetDir "configs"
        if (-not (Test-Path $dstConfigs)) {
            New-Item -ItemType Directory -Path $dstConfigs -Force | Out-Null
        }

        Invoke-Robocopy $configs, $dstConfigs, "/E", "/NFL", "/NDL", "/NJH", "/NJS"
        $restored = $true
    }

    foreach ($extra in @("imgui.ini", "migration-notice.dismissed")) {
        $src = Join-Path $BackupDir $extra
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $TargetDir $extra) -Force
            $restored = $true
        }
    }

    $backupPlugins = Join-Path $BackupDir "Plugins"
    if (Test-Path $backupPlugins) {
        foreach ($plugin in Get-ChildItem $backupPlugins -Directory) {
            $srcConfig = Join-Path $plugin.FullName "config"
            if (-not (Test-Path $srcConfig)) {
                continue
            }

            $dstConfig = Join-Path $TargetDir "Plugins\$($plugin.Name)\config"
            if (-not (Test-Path $dstConfig)) {
                New-Item -ItemType Directory -Path $dstConfig -Force | Out-Null
            }

            Invoke-Robocopy $srcConfig, $dstConfig, "/E", "/NFL", "/NDL", "/NJH", "/NJS"
            $restored = $true
        }
    }

    return $restored
}

function Seed-PluginDefaultConfigs {
    param(
        [string]$ProjectRoot,
        [string]$TargetPublishDir
    )

    foreach ($pluginDir in Get-ChildItem (Join-Path $ProjectRoot "Plugins") -Directory) {
        $srcSettings = Join-Path $pluginDir.FullName "config\settings.txt"
        if (-not (Test-Path $srcSettings)) {
            continue
        }

        $dstDir = Join-Path $TargetPublishDir "Plugins\$($pluginDir.Name)\config"
        $dstSettings = Join-Path $dstDir "settings.txt"
        if (Test-Path $dstSettings) {
            continue
        }

        if (-not (Test-Path $dstDir)) {
            New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
        }

        Copy-Item $srcSettings $dstSettings -Force
        Write-Host "  Default-Config kopiert: $($pluginDir.Name)" -ForegroundColor DarkGray

        foreach ($extra in Get-ChildItem (Join-Path $pluginDir.FullName "config") -File -Filter "*.json") {
            $dstExtra = Join-Path $dstDir $extra.Name
            if (-not (Test-Path $dstExtra)) {
                Copy-Item $extra.FullName $dstExtra -Force
                Write-Host "  Default-Config kopiert: $($pluginDir.Name)\config\$($extra.Name)" -ForegroundColor DarkGray
            }
        }
    }
}

function Get-BlockingGameHelperProcesses {
    param([string]$DeployDir)

    if (-not (Test-Path $DeployDir)) {
        return @()
    }

    $deployRoot = [System.IO.Path]::GetFullPath($DeployDir).TrimEnd('\')
    $processNames = @('GameHelper', 'GameHelper.App')
    $blocking = @()

    foreach ($proc in Get-Process -ErrorAction SilentlyContinue) {
        if ($processNames -notcontains $proc.ProcessName) {
            continue
        }

        try {
            $exePath = $proc.MainModule.FileName
        }
        catch {
            continue
        }

        if ([string]::IsNullOrWhiteSpace($exePath)) {
            continue
        }

        $exeDir = [System.IO.Path]::GetFullPath((Split-Path $exePath -Parent)).TrimEnd('\')
        if ($exeDir.Equals($deployRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $blocking += $proc
        }
    }

    return $blocking
}

function Remove-DeployDirectory {
    param([string]$TargetDir)

    $blockers = Get-BlockingGameHelperProcesses -DeployDir $TargetDir
    if ($blockers.Count -gt 0) {
        $details = ($blockers | ForEach-Object { "$($_.ProcessName).exe (PID $($_.Id))" }) -join ', '
        throw @"
Deploy nach '$TargetDir' blockiert: GameHelper laeuft noch aus diesem Ordner ($details).
Bitte GameHelper schliessen und rebuild-test erneut ausfuehren.
"@
    }

    if (-not (Test-Path $TargetDir)) {
        return
    }

    try {
        Remove-Item $TargetDir -Recurse -Force -ErrorAction Stop
    }
    catch {
        throw @"
Ordner '$TargetDir' konnte nicht geleert werden. Meist blockiert ein noch laufender GameHelper oder Antivirus die DLLs.
Bitte GameHelper schliessen, ggf. PoE beenden, und rebuild-test erneut starten.
Details: $($_.Exception.Message)
"@
    }
}

function Test-LauncherDeploy {
    param([string]$TargetPublishDir)

    $required = @(
        "GameHelper.exe",
        "AsmResolver.dll",
        "AsmResolver.PE.dll",
        "AsmResolver.PE.File.dll",
        "AsmResolver.PE.Win32Resources.dll"
    )

    $missing = @()
    foreach ($file in $required) {
        if (-not (Test-Path (Join-Path $TargetPublishDir $file))) {
            $missing += $file
        }
    }

    if ($missing.Count -gt 0) {
        throw "Launcher-Dateien fehlen in $TargetPublishDir`: $($missing -join ', '). Bitte Solution neu bauen (Launcher-Projekt)."
    }
}

function Test-PluginDeploy {
    param([string]$TargetPublishDir)

    $required = @(
        @{ Plugin = "Autopot"; Files = @("AutoPot.dll") },
        @{ Plugin = "HealthBars"; Files = @("HealthBars.dll", "Textures\full_bar.png", "Textures\hollow_bar.png") },
        @{ Plugin = "Radar"; Files = @("Radar.dll", "icons.png", "important_tgt_files.txt") },
        @{ Plugin = "RitualHelper"; Files = @("RitualHelper.dll", "item_names.json") },
        @{ Plugin = "RunecraftHelper"; Files = @("RunecraftHelper.dll") },
        @{ Plugin = "SekhemaHelper"; Files = @("SekhemaHelper.dll") },
        @{ Plugin = "AuraTracker"; Files = @("AuraTracker.dll") },
        @{ Plugin = "KikiMeter"; Files = @("KikiMeter.dll") },
        @{ Plugin = "CorpseCleaner"; Files = @("CorpseCleaner.dll", "CorpseCleaner.deps.json", "LibBundle3.dll", "LibGGPK3.dll", "LibBundledGGPK3.dll", "SystemExtensions.dll", "LICENSE") },
        @{ Plugin = "Atlas"; Files = @("Atlas.dll") },
        @{ Plugin = "LootTracker"; Files = @("LootTracker.dll") },
        @{ Plugin = "LootValue"; Files = @("LootValue.dll") },
        @{ Plugin = "StashValueByZx0"; Files = @("StashValueByZx0.dll") },
        @{ Plugin = "AmanamuVoidAlert"; Files = @("AmanamuVoidAlert.dll") },
        @{ Plugin = "PlayerBuffBar"; Files = @("PlayerBuffBar.dll") },
        @{ Plugin = "SimpleBars"; Files = @("SimpleBars.dll", "Textures\full_bar.png", "Textures\hollow_bar.png") },
        @{ Plugin = "Hiveblood"; Files = @("Hiveblood.dll") }
    )

    foreach ($entry in $required) {
        $pluginDir = Join-Path $TargetPublishDir "Plugins\$($entry.Plugin)"
        foreach ($file in $entry.Files) {
            $path = Join-Path $pluginDir $file
            if (-not (Test-Path $path)) {
                Write-Warning "Plugin-Deploy unvollstaendig: $($entry.Plugin)\$file fehlt in $TargetPublishDir"
            }
        }
    }
}

function Repair-PluginsJson {
    param([string]$JsonPath)

    if (-not (Test-Path $JsonPath)) {
        return
    }

    $raw = Get-Content $JsonPath -Raw
    if ($raw -notmatch 'Autopot') {
        return
    }

    $json = $raw | ConvertFrom-Json
    $changed = $false

    if ($json.PSObject.Properties.Name -contains "Autopot") {
        $legacy = $json.Autopot
        $json.PSObject.Properties.Remove("Autopot")
        if ($json.PSObject.Properties.Name -notcontains "AutoPot") {
            $json | Add-Member -NotePropertyName "AutoPot" -NotePropertyValue $legacy -Force
        }
        $changed = $true
    }

    if ($json.PSObject.Properties.Name -notcontains "SekhemaHelper") {
        $json | Add-Member -NotePropertyName "SekhemaHelper" -NotePropertyValue @{ Enable = $true } -Force
        $changed = $true
    }

    if ($json.PSObject.Properties.Name -notcontains "AuraTracker") {
        $json | Add-Member -NotePropertyName "AuraTracker" -NotePropertyValue @{ Enable = $true; AutoStart = $true } -Force
        $changed = $true
    }

    if ($json.PSObject.Properties.Name -notcontains "Hiveblood") {
        $json | Add-Member -NotePropertyName "Hiveblood" -NotePropertyValue @{ Enable = $true; AutoStart = $true } -Force
        $changed = $true
    }

    if ($json.PSObject.Properties.Name -notcontains "AmanamuVoidAlert") {
        $json | Add-Member -NotePropertyName "AmanamuVoidAlert" -NotePropertyValue @{ Enable = $true; AutoStart = $true } -Force
        $changed = $true
    }

    if ($json.PSObject.Properties.Name -notcontains "PlayerBuffBar") {
        $json | Add-Member -NotePropertyName "PlayerBuffBar" -NotePropertyValue @{ Enable = $true; AutoStart = $true } -Force
        $changed = $true
    }

    if ($json.PSObject.Properties.Name -notcontains "SimpleBars") {
        $json | Add-Member -NotePropertyName "SimpleBars" -NotePropertyValue @{ Enable = $false; AutoStart = $false } -Force
        $changed = $true
    }

    if (-not $changed) {
        return
    }

    $json | ConvertTo-Json -Depth 5 | Set-Content $JsonPath -Encoding UTF8
    Write-Host "  plugins.json migriert ($JsonPath)" -ForegroundColor DarkYellow
}

if (-not (Test-Path $Solution)) {
    Write-Host "Solution nicht gefunden. Fuehre zuerst setup-project.ps1 aus." -ForegroundColor Red
    & (Join-Path $Root "setup-project.ps1")
}

Write-Host "=== Build ($Configuration) -> $PublishDir ===" -ForegroundColor Cyan
Push-Location $Root
try {
    Repair-PluginsJson -JsonPath (Join-Path $Root "runtime-backup\configs\plugins.json")

    dotnet restore $Solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore fehlgeschlagen" }

    dotnet build $Solution -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build fehlgeschlagen" }

    $outDir = Join-Path $Root "GameHelper\bin\$Configuration\net10.0-windows\win-x64"
    if (-not (Test-Path $outDir)) {
        throw "Build-Ausgabe nicht gefunden: $outDir"
    }

    $defaultPublish = Join-Path $Root "publish"
    $testUserBackup = Join-Path $Root "test-runtime-backup"
    if ($PublishDir -ne $defaultPublish -and (Test-Path $PublishDir)) {
        Write-Host "Sichere Test-Einstellungen vor dem Neu-Deploy ..." -ForegroundColor DarkGray
        if (Test-Path $testUserBackup) {
            Remove-Item $testUserBackup -Recurse -Force
        }

        Save-DeployUserData -SourceDir $PublishDir -BackupDir $testUserBackup
    }

    Remove-DeployDirectory -TargetDir $PublishDir
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
    Invoke-Robocopy $outDir, $PublishDir, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/nc", "/ns", "/np"

    Repair-PluginsJson -JsonPath (Join-Path $PublishDir "configs\plugins.json")

    # Runtime-Konfigurationen aus Backup wiederherstellen (falls vorhanden)
    $runtimeBackup = Join-Path $Root "runtime-backup"
    if (Test-Path $runtimeBackup) {
        if (Test-Path (Join-Path $runtimeBackup "configs")) {
            Invoke-Robocopy (Join-Path $runtimeBackup "configs"), (Join-Path $PublishDir "configs"), "/E", "/NFL", "/NDL", "/NJH", "/NJS"
            Repair-PluginsJson -JsonPath (Join-Path $PublishDir "configs\plugins.json")
        }
        foreach ($plugin in Get-ChildItem (Join-Path $PublishDir "Plugins") -Directory) {
            $srcConfig = Join-Path $runtimeBackup "Plugins\$($plugin.Name)\config"
            if (Test-Path $srcConfig) {
                $dstConfig = Join-Path $plugin.FullName "config"
                if (-not (Test-Path $dstConfig)) { New-Item -ItemType Directory -Path $dstConfig | Out-Null }
                Invoke-Robocopy $srcConfig, $dstConfig, "/E", "/NFL", "/NDL", "/NJH", "/NJS"
            }
        }
    }

    if ($PublishDir -ne $defaultPublish) {
        if (Restore-DeployUserData -TargetDir $PublishDir -BackupDir $testUserBackup) {
            Repair-PluginsJson -JsonPath (Join-Path $PublishDir "configs\plugins.json")
            Write-Host "  Test-Einstellungen wiederhergestellt." -ForegroundColor DarkGray
        }
        elseif (Test-Path $defaultPublish) {
            $dstConfigs = Join-Path $PublishDir "configs"
            if (-not (Test-Path (Join-Path $dstConfigs "plugins.json"))) {
                $srcConfigs = Join-Path $defaultPublish "configs"
                if (Test-Path $srcConfigs) {
                    if (-not (Test-Path $dstConfigs)) { New-Item -ItemType Directory -Path $dstConfigs | Out-Null }
                    Invoke-Robocopy $srcConfigs, $dstConfigs, "/E", "/NFL", "/NDL", "/NJH", "/NJS"
                    Repair-PluginsJson -JsonPath (Join-Path $dstConfigs "plugins.json")
                    Write-Host "  Configs aus publish\ uebernommen (erster Test-Build)." -ForegroundColor DarkGray
                }
            }

            foreach ($plugin in Get-ChildItem (Join-Path $PublishDir "Plugins") -Directory -ErrorAction SilentlyContinue) {
                $dstConfig = Join-Path $plugin.FullName "config"
                if (Test-Path (Join-Path $dstConfig "settings.txt")) { continue }
                $srcConfig = Join-Path $defaultPublish "Plugins\$($plugin.Name)\config"
                if (-not (Test-Path $srcConfig)) { continue }
                if (-not (Test-Path $dstConfig)) { New-Item -ItemType Directory -Path $dstConfig | Out-Null }
                Invoke-Robocopy $srcConfig, $dstConfig, "/E", "/NFL", "/NDL", "/NJH", "/NJS"
                Write-Host "  Plugin-Config aus publish\: $($plugin.Name)" -ForegroundColor DarkGray
            }
        }
    }

    Seed-PluginDefaultConfigs -ProjectRoot $Root -TargetPublishDir $PublishDir
    Test-LauncherDeploy -TargetPublishDir $PublishDir
    Test-PluginDeploy -TargetPublishDir $PublishDir

    # update.config.json wird nicht mehr benoetigt (oeffentliche GitHub Releases).

    $appExe = Join-Path $PublishDir "GameHelper.App.exe"
    if (-not (Test-Path $appExe)) { $appExe = Join-Path $PublishDir "GameHelper.exe" }
    $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($appExe)
    $buildVersion = if (-not [string]::IsNullOrEmpty($Version)) {
        $Version
    }
    elseif ($vi.FileVersion) {
        ($vi.FileVersion -split '\.')[0..2] -join '.'
    }
    else {
        Get-ProjectVersion -Root $Root
    }

    $historySrc = Join-Path $Root "changelog-history.json"
    if (Test-Path $historySrc) {
        Copy-Item $historySrc (Join-Path $PublishDir "changelog-history.json") -Force
    }

    # github.config.json nicht nach publish\ kopieren (Release nutzt eingebettetes Default-Repo).

    Write-BuildInfoFiles -PublishDir $PublishDir -Version $buildVersion -Source "local-build"

    $updateState = [ordered]@{
        lastPublished = (Get-Date).ToUniversalTime().ToString("o")
        lastVersion   = $buildVersion
        source        = "local-build"
    }
    $updateState | ConvertTo-Json | Set-Content (Join-Path $PublishDir "update.state.json") -Encoding UTF8
    Write-Host "  update.state.json gesetzt." -ForegroundColor DarkYellow

    Write-Host ""
    Write-Host "Build erfolgreich: $PublishDir" -ForegroundColor Green
    Write-Host "Starten mit: $PublishDir\GameHelper.exe" -ForegroundColor Green
}
finally {
    Pop-Location
}

exit 0
