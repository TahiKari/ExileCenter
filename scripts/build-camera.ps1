param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../Plugins/CameraZoom/Native/bin'))
$ErrorActionPreference = 'Stop'
$native = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Plugins/CameraZoom/Native'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
& cmake -S $native -B (Join-Path $output 'build') -A x64
if ($LASTEXITCODE) { throw 'Camera configuration failed.' }
& cmake --build (Join-Path $output 'build') --config Release
if ($LASTEXITCODE) { throw 'Camera compilation failed.' }
$dll = Join-Path $output 'ExileCenter.Camera.dll'
Copy-Item -LiteralPath (Join-Path $output 'build/Release/ExileCenter.Camera.dll') -Destination $dll
(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash | Set-Content -LiteralPath "$dll.sha256" -Encoding ascii
$checks = Join-Path $output 'checks'
[void][IO.Directory]::CreateDirectory($checks)
Copy-Item -LiteralPath $dll -Destination $checks
Copy-Item -LiteralPath (Join-Path $output 'build/Release/CameraFixture.exe') -Destination (Join-Path $checks 'PathOfExileSteam.exe')
Copy-Item -LiteralPath (Join-Path $output 'build/Release/CameraFixture.exe') -Destination (Join-Path $checks 'Unrelated.exe')
Push-Location $checks
try {
    function Config([uint32]$enabled, [single]$minimum, [single]$maximum) {
        $bytes = [BitConverter]::GetBytes([uint32]0x315A4345) + [BitConverter]::GetBytes([uint32]20) +
            [BitConverter]::GetBytes($enabled) + [BitConverter]::GetBytes($minimum) + [BitConverter]::GetBytes($maximum)
        [IO.File]::WriteAllBytes((Join-Path $checks 'ExileCenter.Camera.bin'), $bytes)
    }
    Config 1 0.25 2
    & ./PathOfExileSteam.exe 0.25 2 1
    if ($LASTEXITCODE) { throw "Camera bounds/update checks failed ($LASTEXITCODE)." }
    Config 0 0.25 2
    & ./PathOfExileSteam.exe 0 1 1
    if ($LASTEXITCODE) { throw 'Camera disabled-start checks failed.' }
    Config 1 2 1
    & ./PathOfExileSteam.exe 0 1 0
    if ($LASTEXITCODE -or (Get-Content 'ExileCenter.Camera.status') -ne 'invalid-config') { throw 'Invalid configuration not refused.' }
    Config 1 0.25 2
    & ./Unrelated.exe 0 1 0
    if ($LASTEXITCODE) { throw 'Unrelated process was modified.' }
    # Verify the actual version.dll proxy name and Windows import search, too.
    Copy-Item -LiteralPath $dll -Destination (Join-Path $checks 'version.dll')
    Copy-Item -LiteralPath (Join-Path $output 'build/Release/CameraChecks.exe') -Destination (Join-Path $checks 'PathOfExileSteam.exe') -Force
    Config 1 0.25 2
    & ./PathOfExileSteam.exe 0.25 2 1
    if ($LASTEXITCODE) { throw "Windows proxy startup checks failed ($LASTEXITCODE)." }
} finally { Pop-Location }
