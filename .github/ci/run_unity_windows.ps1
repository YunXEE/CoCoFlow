$ErrorActionPreference = 'Stop'
$config = Get-Content '.github/ci/unity-host.json' -Raw | ConvertFrom-Json
$nativeLog = Join-Path $env:RUNNER_TEMP 'gameci-native-test.log'
$returnPattern = 'Successfully returned (the entitlement|ULF|floating) license|License has been returned'
$unityTestExitCode = 1
$returnSource = 'gameci'
$returnExitCode = $null
$returnLogAvailable = $false
$returnDiagnostics = @()
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
        # Use the complete Personal command from official return_license.ps1.
        # This returns the existing seat; it does not activate another one.
        $returnSource = 'editor-fallback'
        $scratch = Join-Path $env:RUNNER_TEMP 'unity-return'
        New-Item -ItemType Directory $scratch -Force | Out-Null
        $returnLog = Join-Path $scratch 'return.log'
        $editor = Join-Path $env:UNITY_PATH 'Editor/Unity.exe'
        & pwsh -NoProfile -NonInteractive -File $launcher $editor -logFile $returnLog -quit -returnlicense -username $env:UNITY_EMAIL -password $env:UNITY_PASSWORD -projectPath $scratch | Out-Host
        $returnExitCode = $LASTEXITCODE
        $returnLogAvailable = Test-Path $returnLog
        $returnText = if ($returnLogAvailable) { Get-Content $returnLog -Raw } else { '' }
        $returned = $returnText -match $returnPattern
        # Keep only known diagnostic categories; raw licensing logs contain credentials.
        $patterns = [ordered]@{
            'missing-ulf' = 'Ulf license file not found'
            'access-token-unavailable' = 'Access token is unavailable'
            'machine-bindings-mismatch' = "Machine bindings don't match"
            'license-client-unavailable' = 'Connection attempt to the License Client.*failed|Failed to acquire global mutex'
            'no-active-license' = 'No license activation found|No valid Unity Editor license found|License is not active'
        }
        $returnDiagnostics = @($patterns.Keys | Where-Object { $returnText -match $patterns[$_] })
    }
    if ($activated) {
        @{
            confirmed = $returned
            platform = 'WindowsEditor'
            source = $returnSource
            exitCode = $returnExitCode
            logAvailable = $returnLogAvailable
            diagnostics = $returnDiagnostics
        } |
            ConvertTo-Json | Set-Content 'ci-results/license-return.json'
        if (-not $returned) { throw 'The activated Personal seat was not confirmed returned.' }
    } else {
        # A rejected activation stops here, without any further credential-bearing command.
        $unityTestExitCode = 1
    }
}
exit $unityTestExitCode
