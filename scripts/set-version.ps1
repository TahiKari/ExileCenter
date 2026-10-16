# Hilfsfunktionen fuer Versionsnummern in .csproj und publish/

function Get-ProjectVersion {
    param([string]$Root)

    $csproj = Join-Path $Root "ExileCenter\ExileCenter.csproj"
    if (-not (Test-Path $csproj)) {
        return "1.0.0"
    }

    $xml = Get-Content $csproj -Raw
    if ($xml -match '<Version>([^<]+)</Version>') {
        return $Matches[1].Trim()
    }

    return "1.0.0"
}

function Test-VersionFormat {
    param([string]$Version)

    return $Version -match '^\d+\.\d+\.\d+$'
}

function Compare-ProjectVersion {
    param(
        [string]$Left,
        [string]$Right
    )

    $normalize = {
        param([string]$v)
        ($v.Trim().TrimStart('v').Split('.') | ForEach-Object {
            if ($_ -match '^\d+$') { [int]$_ } else { 0 }
        })
    }

    $a = & $normalize $Left
    $b = & $normalize $Right
    $len = [Math]::Max($a.Count, $b.Count)
    for ($i = 0; $i -lt $len; $i++) {
        $av = if ($i -lt $a.Count) { $a[$i] } else { 0 }
        $bv = if ($i -lt $b.Count) { $b[$i] } else { 0 }
        if ($av -lt $bv) { return -1 }
        if ($av -gt $bv) { return 1 }
    }

    return 0
}

function Set-ProjectVersion {
    param(
        [string]$Root,
        [string]$Version
    )

    if (-not (Test-VersionFormat $Version)) {
        throw "Ungueltige Version '$Version'. Format: z.B. 1.0.1"
    }

    $parts = $Version.Split('.')
    $assemblyVersion = "$($parts[0]).$($parts[1]).$($parts[2]).0"
    $fileVersion = $assemblyVersion

    $projects = @(
        (Join-Path $Root "ExileCenter\ExileCenter.csproj"),
        (Join-Path $Root "Launcher\Launcher.csproj")
    )

    foreach ($csproj in $projects) {
        if (-not (Test-Path $csproj)) {
            Write-Warning "Ueberspringe: $csproj"
            continue
        }

        $xml = Get-Content $csproj -Raw
        $xml = $xml -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
        $xml = $xml -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$assemblyVersion</AssemblyVersion>"
        $xml = $xml -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$fileVersion</FileVersion>"
        Set-Content $csproj $xml -Encoding UTF8
        Write-Host "  Version $Version -> $(Split-Path $csproj -Parent | Split-Path -Leaf)" -ForegroundColor DarkGray
    }
}

function Prompt-VersionInput {
    param([string]$Root)

    $current = Get-ProjectVersion -Root $Root
    Write-Host ""
    Write-Host "Versionsnummer fuer diesen Build" -ForegroundColor Cyan
    Write-Host "  Aktuell in den Projekten: $current" -ForegroundColor DarkGray
    Write-Host "  Format: Haupt.Neben.Patch (z.B. 1.0.2)" -ForegroundColor DarkGray
    Write-Host "  Leer lassen = $current beibehalten" -ForegroundColor DarkGray
    Write-Host ""

    $answer = Read-Host "Neue Versionsnummer"
    if ([string]::IsNullOrWhiteSpace($answer)) {
        return $current
    }

    $answer = $answer.Trim()
    if (-not (Test-VersionFormat $answer)) {
        throw "Ungueltige Eingabe '$answer'. Bitte Format x.y.z verwenden."
    }

    return $answer
}

function Get-ReleaseNotesLines {
    param([string]$Root)

    $notesFile = Join-Path $Root "release-notes.txt"
    if (-not (Test-Path $notesFile)) {
        return @()
    }

    return @(Get-Content $notesFile |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.Trim().StartsWith('#') } |
        ForEach-Object { $_.Trim() })
}

function Clear-ReleaseNotes {
    param([string]$Root)

    $notesFile = Join-Path $Root "release-notes.txt"
    Set-Content $notesFile "" -Encoding UTF8
    Write-Host "  release-notes.txt geleert (vor dem naechsten Release neu befuellen)." -ForegroundColor DarkGray
}

function Update-ChangelogHistory {
    param(
        [string]$Root,
        [string]$Version,
        [string]$Published,
        [string[]]$Changelog
    )

    $path = Join-Path $Root "changelog-history.json"
    $releases = [System.Collections.Generic.List[object]]::new()

    if (Test-Path $path) {
        try {
            $existing = Get-Content $path -Raw | ConvertFrom-Json
            foreach ($entry in @($existing.releases)) {
                if ($null -eq $entry) { continue }
                if ([string]$entry.version -eq $Version) { continue }
                $releases.Add($entry)
            }
        }
        catch {
            Write-Host "  Warnung: changelog-history.json ungueltig, wird neu aufgebaut." -ForegroundColor DarkYellow
        }
    }

    $newEntry = [ordered]@{
        version   = $Version
        published = $Published
        changelog = @($Changelog)
    }
    $releases.Insert(0, $newEntry)

    $sorted = $releases | Sort-Object {
        $v = [string]$_.version -replace '^v', ''
        try { [version]$v } catch { [version]'0.0.0' }
    } -Descending

    ([ordered]@{ releases = @($sorted) } | ConvertTo-Json -Depth 8) | Set-Content $path -Encoding UTF8
    Write-Host "  changelog-history.json archiviert ($Version, $($Changelog.Count) Punkte)." -ForegroundColor DarkGray
}

function New-BilingualChangelogLine {
    param(
        [string]$English,
        [string]$German
    )

    return "$English || $German"
}

function Test-IsGenericChangelogLine {
    param([string]$Line)

    if ([string]::IsNullOrWhiteSpace($Line)) {
        return $true
    }

    $en = $Line
    if ($Line -match ' \|\| ') {
        $en = ($Line -split ' \|\| ', 2)[0].Trim()
    }

    $generic = @(
        'Improvements and bug fixes in this version.',
        'Plugins and settings were updated.',
        'Improvements and bug fixes.',
        'Version '
    )

    foreach ($pattern in $generic) {
        if ($en.Equals($pattern, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }

        if ($pattern -eq 'Version ' -and $en.StartsWith('Version ', [StringComparison]::OrdinalIgnoreCase) -and
            $en.EndsWith(' includes improvements and bug fixes.', [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Get-PreviousReleaseVersion {
    param(
        [string]$Root,
        [string]$CurrentVersion
    )

    $path = Join-Path $Root "changelog-history.json"
    if (-not (Test-Path $path)) {
        return $null
    }

    try {
        $existing = Get-Content $path -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }

    $current = [version](([string]$CurrentVersion) -replace '^v', '')
    $best = $null
    $bestVer = [version]'0.0.0'

    foreach ($entry in @($existing.releases)) {
        if ($null -eq $entry -or [string]::IsNullOrWhiteSpace($entry.version)) {
            continue
        }

        $vStr = ([string]$entry.version) -replace '^v', ''
        try {
            $v = [version]$vStr
        }
        catch {
            continue
        }

        if ($v -ge $current) {
            continue
        }

        if ($v -gt $bestVer) {
            $bestVer = $v
            $best = [string]$entry.version
        }
    }

    return $best
}

function Get-RemoteManifestFileMap {
    param(
        [string]$Repository,
        [string]$ReleaseVersion
    )

    $map = @{}
    if ([string]::IsNullOrWhiteSpace($ReleaseVersion)) {
        return $map
    }

    $tag = if ($ReleaseVersion -match '^v') { $ReleaseVersion } else { "v$ReleaseVersion" }
    $url = "https://github.com/$Repository/releases/download/$tag/manifest.json"
    $json = $null

    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $response = Invoke-WebRequest -Uri $url -Headers @{ 'User-Agent' = 'GameHelper-Publish' } -UseBasicParsing -TimeoutSec 30
        $json = $response.Content
    }
    catch {
        $tmp = Join-Path $env:TEMP "gamehelper-changelog-$tag.json"
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($curl) {
            & $curl.Source -sL -o $tmp $url 2>$null
            if ((Test-Path $tmp) -and (Get-Item $tmp).Length -gt 0) {
                $json = Get-Content $tmp -Raw -Encoding UTF8
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($json)) {
        Write-Host ("  Kein Remote-Manifest fuer Changelog ({0})." -f $tag) -ForegroundColor DarkGray
        return $map
    }

    try {
        $manifest = $json | ConvertFrom-Json
        foreach ($entry in @($manifest.files)) {
            if ($entry.path -and $entry.hash) {
                $map[[string]$entry.path] = [string]$entry.hash
            }
        }
    }
    catch {
        Write-Host ("  Remote-Manifest ungueltig ({0})." -f $tag) -ForegroundColor DarkYellow
    }

    return $map
}

function Build-AutoChangelogFromFiles {
    param(
        [string]$Root,
        [string]$Repository,
        [string]$Version,
        [array]$CurrentFiles
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $prevVersion = Get-PreviousReleaseVersion -Root $Root -CurrentVersion $Version
    if (-not $prevVersion) {
        return @()
    }

    Write-Host ("  Changelog-Diff gegen v{0} ..." -f (($prevVersion) -replace '^v', '')) -ForegroundColor DarkGray
    $prevMap = Get-RemoteManifestFileMap -Repository $Repository -ReleaseVersion $prevVersion
    if ($prevMap.Count -eq 0) {
        return @()
    }

    $pluginDlls = @{}
    $pluginData = @{}
    $pluginNew = @{}
    $launcherChanged = $false
    $coreChanged = $false
    $downloaderChanged = $false
    $otherChanged = [System.Collections.Generic.List[string]]::new()

    foreach ($entry in $CurrentFiles) {
        $path = [string]$entry.path
        $hash = [string]$entry.hash
        $prevHash = $prevMap[$path]

        if ($prevHash -and $prevHash -eq $hash) {
            continue
        }

        if ($path -match '^Plugins/([^/]+)/(.+)$') {
            $plugin = $Matches[1]
            $file = $Matches[2]
            if ($file -match '\.dll$', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) {
                if (-not $pluginDlls.ContainsKey($plugin)) { $pluginDlls[$plugin] = [System.Collections.Generic.List[string]]::new() }
                $pluginDlls[$plugin].Add($file)
            }
            elseif (-not $prevHash) {
                if (-not $pluginNew.ContainsKey($plugin)) { $pluginNew[$plugin] = [System.Collections.Generic.List[string]]::new() }
                $pluginNew[$plugin].Add($file)
            }
            else {
                if (-not $pluginData.ContainsKey($plugin)) { $pluginData[$plugin] = [System.Collections.Generic.List[string]]::new() }
                $pluginData[$plugin].Add($file)
            }
            continue
        }

        switch -Regex ($path) {
            '^GameHelper\.exe$|^GameHelper\.dll$' { $launcherChanged = $true; break }
            '^GameHelper\.App\.(exe|dll)$' { $coreChanged = $true; break }
            '^GameHelperDownloader\.exe$' { $downloaderChanged = $true; break }
            default {
                if (-not $prevHash) {
                    $otherChanged.Add("New: $path")
                }
                else {
                    $otherChanged.Add("Updated: $path")
                }
            }
        }
    }

    if ($launcherChanged -or $coreChanged) {
        $parts = @()
        if ($launcherChanged) { $parts += 'Launcher' }
        if ($coreChanged) { $parts += 'GameHelper core' }
        $en = "Core: $($parts -join ', ') updated"
        $de = "Kern: $($parts -join ', ') aktualisiert"
        $lines.Add((New-BilingualChangelogLine $en $de))
    }

    if ($downloaderChanged) {
        $lines.Add((New-BilingualChangelogLine `
            'Downloader: GameHelperDownloader.exe updated' `
            'Downloader: GameHelperDownloader.exe aktualisiert'))
    }

    foreach ($plugin in ($pluginDlls.Keys + $pluginData.Keys + $pluginNew.Keys | Sort-Object -Unique)) {
        $hasDll = $pluginDlls.ContainsKey($plugin)
        $dataFiles = if ($pluginData.ContainsKey($plugin)) { @($pluginData[$plugin]) } else { @() }
        $newFiles = if ($pluginNew.ContainsKey($plugin)) { @($pluginNew[$plugin]) } else { @() }

        if ($hasDll -and $dataFiles.Count -eq 0 -and $newFiles.Count -eq 0) {
            $en = "Plugin ${plugin}: code updated"
            $de = "Plugin ${plugin}: Code aktualisiert"
        }
        elseif ($hasDll) {
            $extra = @($dataFiles + $newFiles | Select-Object -First 3) -join ', '
            if (($dataFiles.Count + $newFiles.Count) -gt 3) { $extra += ', ...' }
            $en = "Plugin ${plugin}: code + data updated ($extra)"
            $de = "Plugin ${plugin}: Code und Daten aktualisiert ($extra)"
        }
        elseif ($newFiles.Count -gt 0) {
            $extra = ($newFiles | Select-Object -First 3) -join ', '
            if ($newFiles.Count -gt 3) { $extra += ', ...' }
            $en = "Plugin ${plugin}: new files ($extra)"
            $de = "Plugin ${plugin}: neue Dateien ($extra)"
        }
        else {
            $extra = ($dataFiles | Select-Object -First 3) -join ', '
            if ($dataFiles.Count -gt 3) { $extra += ', ...' }
            $en = "Plugin ${plugin}: data updated ($extra)"
            $de = "Plugin ${plugin}: Daten aktualisiert ($extra)"
        }

        $lines.Add((New-BilingualChangelogLine $en $de))
    }

    foreach ($item in ($otherChanged | Select-Object -First 8)) {
        if ($item.StartsWith('New: ')) {
            $path = $item.Substring(5)
            $lines.Add((New-BilingualChangelogLine "New file: $path" "Neue Datei: $path"))
        }
        else {
            $path = $item.Substring(9)
            $lines.Add((New-BilingualChangelogLine "Updated: $path" "Aktualisiert: $path"))
        }
    }

    if ($otherChanged.Count -gt 8) {
        $more = $otherChanged.Count - 8
        $lines.Add((New-BilingualChangelogLine `
            "Additional $more file change(s) in this release" `
            "Weitere $more Datei-Aenderung(en) in diesem Release"))
    }

    return @($lines)
}

function Merge-ReleaseChangelog {
    param(
        [string]$Root,
        [string]$Repository,
        [string]$Version,
        [array]$CurrentFiles,
        [string[]]$UserLines
    )

    $manual = @($UserLines | Where-Object { -not (Test-IsGenericChangelogLine $_) })
    $auto = @(Build-AutoChangelogFromFiles -Root $Root -Repository $Repository -Version $Version -CurrentFiles $CurrentFiles)

    $merged = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $manual) {
        if (-not $merged.Contains($line)) {
            $merged.Add($line)
        }
    }

    if ($auto.Count -gt 0) {
        if ($merged.Count -gt 0) {
            $merged.Add((New-BilingualChangelogLine `
                '--- Changed components (auto-detected) ---' `
                '--- Geaenderte Komponenten (automatisch erkannt) ---'))
        }

        foreach ($line in $auto) {
            if (-not $merged.Contains($line)) {
                $merged.Add($line)
            }
        }
    }

    if ($merged.Count -eq 0) {
        $merged.Add((New-BilingualChangelogLine `
            "Version $Version release" `
            "Version $Version Release"))
    }

    Write-Host ("  Changelog: {0} manuell, {1} automatisch, {2} gesamt." -f $manual.Count, $auto.Count, $merged.Count) -ForegroundColor DarkGray
    return @($merged)
}

function Prompt-ChangelogInput {
    Write-Host ""
    Write-Host "Aenderungen fuer Spieler (Update-Fenster)" -ForegroundColor Cyan
    Write-Host "  Eine Zeile pro Punkt. Leer = nur automatische Erkennung (Plugins/Dateien vs. letztes Release)" -ForegroundColor DarkGray
    Write-Host "  Zweisprachig: Englisch || Deutsch" -ForegroundColor DarkGray
    Write-Host "  Beispiele:" -ForegroundColor DarkGray
    Write-Host "    [Fix] AutoHotKeyTrigger: warning when no profile selected || [Fix] AutoHotKeyTrigger: Warnung ohne Profil" -ForegroundColor DarkGray
    Write-Host "    [New] Activity log for key presses || [Neu] Aktivitaets-Log fuer Tasteneingaben" -ForegroundColor DarkGray
    Write-Host "  Tipp: release-notes.txt vor dem Build befuellen (wird nach Publish geleert)." -ForegroundColor DarkGray
    Write-Host ""

    $lines = @()
    while ($true) {
        $line = Read-Host "  Zeile (Enter = fertig)"
        if ([string]::IsNullOrWhiteSpace($line)) {
            break
        }

        $lines += $line.Trim()
    }

    return $lines
}

function Write-BuildInfoFiles {
    param(
        [string]$PublishDir,
        [string]$Version,
        [string]$Source = "build"
    )

    $builtAt = (Get-Date).ToUniversalTime().ToString("o")
  $versionTxt = @"
GameHelper $Version
Gebaut am: $builtAt (UTC)
Quelle: $Source

Pruefen: Rechtsklick auf GameHelper.App.exe -> Details -> Dateiversion
"@

    $versionTxt | Set-Content (Join-Path $PublishDir "VERSION.txt") -Encoding UTF8

    $distributionTxt = @"
=== GameHelper Verteilung ===

Version: $Version
Gebaut:  $builtAt (UTC)

WICHTIG fuer den Empfaenger:
1. In einen LEEREN Ordner entpacken (nicht ueber eine alte Installation legen).
2. VERSION.txt pruefen - muss $Version zeigen.
3. Beim ersten Start kann kurz ein schwarzes Fenster erscheinen (Update-Pruefung).

Warum manchmal eine aeltere Version erscheint:
- Der Auto-Updater laedt von GitHub Releases nach, wenn dort ein neueres manifest.json liegt.
- Wird ueber eine alte Installation entpackt, bleiben alte Dateien liegen.

Plugin-Credits: In GameHelper unter Plugins -> Spalte Ersteller (siehe CREDITS.md).

Nach dem Entpacken: VERSION.txt oeffnen und Version ablesen.
"@

    $distributionTxt | Set-Content (Join-Path $PublishDir "VERTEILUNG-HINWEIS.txt") -Encoding UTF8

    $creditsSrc = Join-Path (Split-Path $PSScriptRoot -Parent) "CREDITS.md"
    if (Test-Path $creditsSrc) {
        Copy-Item $creditsSrc (Join-Path $PublishDir "CREDITS.md") -Force
    }
}
