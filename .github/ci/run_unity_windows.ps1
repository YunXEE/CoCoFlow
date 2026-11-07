$ErrorActionPreference = 'Stop'
$config = Get-Content '.github/ci/unity-host.json' -Raw | ConvertFrom-Json
$nativeLog = Join-Path $env:RUNNER_TEMP 'gameci-native-test.log'
$returnPattern = 'Successfully returned (the entitlement|ULF|floating) license|License has been returned'
$unityTestExitCode = 1
$launcher = (Resolve-Path '.github/ci/wait_unity_windows.ps1').Path
$env:ENGINE_LAUNCH_WRAPPER = "pwsh -NoProfile -NonInteractive -File $launcher"

try {
    # Official HostRunner: activates, tests both modes, then attempts cleanup.
    & $env:COCOFLOW_GAMECI_EXE test '.ci-work/host' --engine unity --engineVersion $config.unity --docker --local --targetPlatform StandaloneWindows64 --testPlatforms 'playmode;editmode' --artifactsPath ci-results 2>&1 |
        Tee-Object -FilePath $nativeLog
    $unityTestExitCode = $LASTEXITCODE
} finally {
    $text = if (Test-Path $nativeLog) { Get-Content $nativeLog -Raw } else { '' }
    $activated = $text -match '(?m)^Activation complete\.\s*$'
    $returned = $text -match $returnPattern
    if ($activated -and -not $returned) {
        # v0.1.72's one-attempt cleanup can stop at the missing-ULF fallback.
        # Same Personal entitlement return as official windows/steps/return_license.ps1.
        # No account credentials are passed to this Editor invocation.
        $scratch = Join-Path $env:RUNNER_TEMP 'unity-return'
        New-Item -ItemType Directory $scratch -Force | Out-Null
        $returnLog = Join-Path $scratch 'return.log'
        $editor = Join-Path $env:UNITY_PATH 'Editor/Unity.exe'
        & pwsh -NoProfile -NonInteractive -File $launcher $editor -batchmode -quit -returnlicense -projectPath $scratch -logFile $returnLog | Out-Host
        $returnText = if (Test-Path $returnLog) { Get-Content $returnLog -Raw } else { '' }
        $returned = $returnText -match $returnPattern
    }
    if ($activated) {
        @{ confirmed = $returned; platform = 'WindowsEditor' } |
            ConvertTo-Json | Set-Content 'ci-results/license-return.json'
        if (-not $returned) { throw 'The activated Personal seat was not confirmed returned.' }
    } else {
        # A rejected activation stops here, without any further credential-bearing command.
        $unityTestExitCode = 1
    }
}
exit $unityTestExitCode
