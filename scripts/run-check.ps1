# Run local .NET checks through this script so a failed check never interrupts gameplay.
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$CheckAssembly,
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$CheckArguments = @()
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $CheckAssembly -PathType Leaf)) { throw "Missing check: $CheckAssembly" }

# Children inherit this process-local error mode. No Windows-wide settings are changed.
# https://learn.microsoft.com/windows/win32/api/errhandlingapi/nf-errhandlingapi-seterrormode
if (-not ('ExileCenterCheckErrorMode' -as [type])) {
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public static class ExileCenterCheckErrorMode {
    [DllImport("kernel32.dll")] public static extern uint GetErrorMode();
    [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint mode);
}
'@
}
$previousMode = [ExileCenterCheckErrorMode]::GetErrorMode()
$checkExit = 1
try {
    # SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX; failures still go to the console.
    [void][ExileCenterCheckErrorMode]::SetErrorMode($previousMode -bor 3)
    $ErrorActionPreference = 'Continue'
    & dotnet $CheckAssembly @CheckArguments
    $checkExit = $LASTEXITCODE
} finally {
    [void][ExileCenterCheckErrorMode]::SetErrorMode($previousMode)
}
exit $checkExit
