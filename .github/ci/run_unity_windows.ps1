$ErrorActionPreference = 'Stop'
$config = Get-Content '.github/ci/unity-host.json' -Raw | ConvertFrom-Json
$nativeLog = Join-Path $env:RUNNER_TEMP 'gameci-native-test.log'
$launcher = (Resolve-Path '.github/ci/wait_unity_windows.ps1').Path
# Two test Editor processes can each use ten minutes. The step has twenty-two
# minutes, leaving the same job time to run its separate bounded cleanup step.
$env:ENGINE_LAUNCH_WRAPPER = "pwsh -NoProfile -NonInteractive -File $launcher -TimeoutSeconds 600"

& $env:COCOFLOW_GAMECI_EXE test '.ci-work/host' --engine unity --engineVersion $config.unity --docker --local --targetPlatform StandaloneWindows64 --testPlatforms 'playmode;editmode' --artifactsPath ci-results 2>&1 |
    Tee-Object -FilePath $nativeLog
$testExitCode = $LASTEXITCODE
$activationPattern = '(?m)^Activation complete\.\s*$|^Status:\s*\[200\]\s*ASSIGN_SEAT\s*$'
if ($testExitCode -eq 0 -and (Get-Content $nativeLog -Raw) -notmatch $activationPattern) {
    throw 'GameCI returned success without confirmed Personal activation.'
}
exit $testExitCode
