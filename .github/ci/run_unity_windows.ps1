param([switch]$PlayerProbe)
$ErrorActionPreference = 'Stop'
$config = Get-Content '.github/ci/unity-host.json' -Raw | ConvertFrom-Json
$nativeLog = Join-Path $env:RUNNER_TEMP 'gameci-native-test.log'
$launcher = (Resolve-Path '.github/ci/wait_unity_windows.ps1').Path
$probe = if ($PlayerProbe) { (Get-Content '.ci-work/prepared.json' -Raw | ConvertFrom-Json).playerProbe } else { $null }
# Editor suites get ten minutes each; Player probes get thirty, full builds sixty.
# Both step limits leave separate time for bounded license cleanup.
$editorDeadline = if ($PlayerProbe) { if ($probe.scope -eq 'all') { 3600 } else { 1800 } } else { 600 }
$env:ENGINE_LAUNCH_WRAPPER = "pwsh -NoProfile -NonInteractive -File $launcher -TimeoutSeconds $editorDeadline"

$platforms = if ($PlayerProbe) { 'standalone' } else { 'playmode;editmode' }
$gameciArguments = @('test', '.ci-work/host', '--engine', 'unity', '--engineVersion', $config.unity,
    '--docker', '--local', '--targetPlatform', 'StandaloneWindows64',
    '--testPlatforms', $platforms, '--artifactsPath', 'ci-results')
if ($PlayerProbe -and $probe.scope -ne 'all') {
    # '=' is required: a separate value beginning with '-testFilter' is parsed as CLI options.
    $gameciArguments += "--customParameters=-testFilter $($probe.expectedCases -join ';')"
}
& $env:COCOFLOW_GAMECI_EXE @gameciArguments 2>&1 |
    Tee-Object -FilePath $nativeLog
$testExitCode = $LASTEXITCODE
$activationPattern = '(?m)^Activation complete\.\s*$|^Status:\s*\[200\]\s*ASSIGN_SEAT\s*$'
if ($testExitCode -eq 0 -and (Get-Content $nativeLog -Raw) -notmatch $activationPattern) {
    throw 'GameCI returned success without confirmed Personal activation.'
}
exit $testExitCode
