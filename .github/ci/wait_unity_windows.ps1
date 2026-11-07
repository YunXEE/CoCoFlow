param(
    [Parameter(Mandatory, Position = 0)]
    [string]$ExePath,
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Arguments
)
$ErrorActionPreference = 'Stop'

# Windows GUI executables can return control before exiting. GameCI's supported
# ENGINE_LAUNCH_WRAPPER hook must wait before the next test or license return.
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $ExePath
$start.UseShellExecute = $false
foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
$process.WaitForExit()
exit $process.ExitCode
