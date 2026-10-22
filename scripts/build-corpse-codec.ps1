param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../Plugins/CorpseCleaner/Native/bin'))
$ErrorActionPreference = 'Stop'
$native = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Plugins/CorpseCleaner/Native'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$source = Join-Path $output 'source/ooz'
if (!(Test-Path -LiteralPath (Join-Path $source '.git'))) {
    & git init $source
    if ($LASTEXITCODE) { throw 'Cannot initialize decoder source.' }
    & git -C $source remote add origin https://github.com/zao/ooz.git
    if ($LASTEXITCODE) { throw 'Cannot configure decoder source.' }
}
$revision = 'ff5aeb9e45e362e8d6bb1199aa82406285dd2a18'
& git -C $source fetch --depth 1 origin $revision
if ($LASTEXITCODE) { throw 'Cannot fetch pinned decoder source.' }
& git -C $source checkout --detach $revision
if ($LASTEXITCODE) { throw 'Cannot check out pinned decoder source.' }
& git -C $source submodule update --init --depth 1 -- simde
if ($LASTEXITCODE) { throw 'Cannot fetch pinned SIMDe headers.' }
$actual = & git -C (Join-Path $source 'simde') rev-parse HEAD
if ($actual -ne 'dd0b662fd8cf4b1617dbbb4d08aa053e512b08e4') { throw 'Unexpected SIMDe revision.' }
& cmake -S $native -B (Join-Path $output 'build') -A x64 "-DOOZ_ROOT=$source"
if ($LASTEXITCODE) { throw 'Native codec configuration failed.' }
& cmake --build (Join-Path $output 'build') --config Release
if ($LASTEXITCODE) { throw 'Native codec compilation failed.' }
$dll = Join-Path $output 'ExileCenter.ArchiveCodec.dll'
Copy-Item -LiteralPath (Join-Path $output 'build/Release/ExileCenter.ArchiveCodec.dll') -Destination $dll
(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash | Set-Content -LiteralPath "$dll.sha256" -Encoding ascii

# Ship corresponding sources and every required license, not just a binary.
$package = Join-Path $output 'corresponding-source'
[void][IO.Directory]::CreateDirectory((Join-Path $package 'source/ooz/simde'))
foreach ($file in @('kraken.cpp', 'bitknit.cpp', 'lzna.cpp', 'bits_rev_table.h', 'stdafx.h', 'targetver.h')) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $package 'source/ooz')
}
Copy-Item -LiteralPath (Join-Path $source 'simde/simde') -Destination (Join-Path $package 'source/ooz/simde') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $source 'simde/COPYING') -Destination (Join-Path $package 'source/ooz/simde/COPYING')
foreach ($file in @('ArchiveCodec.cpp', 'CMakeLists.txt', 'NOTICE.txt', 'COPYING.txt')) {
    Copy-Item -LiteralPath (Join-Path $native $file) -Destination $package
}
Compress-Archive -Path (Join-Path $package '*') -DestinationPath (Join-Path $output 'ExileCenter.ArchiveCodec-sources.zip') -Force
