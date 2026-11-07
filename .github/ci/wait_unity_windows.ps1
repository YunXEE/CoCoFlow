param(
    [Parameter(Mandatory, Position = 0)]
    [string]$ExePath,
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Arguments,
    [int]$TimeoutSeconds = 0
)
$ErrorActionPreference = 'Stop'

# Windows GUI executables can return control before exiting. GameCI's supported
# ENGINE_LAUNCH_WRAPPER hook must wait before the next test or license return.
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $ExePath
$start.UseShellExecute = $false
foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
if ($TimeoutSeconds -gt 0 -and -not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill($true)
    $process.WaitForExit()
    exit 124
}
$process.WaitForExit()
exit $process.ExitCode
