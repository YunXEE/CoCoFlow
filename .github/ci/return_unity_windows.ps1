$ErrorActionPreference = 'Stop'
$nativeLog = Join-Path $env:RUNNER_TEMP 'gameci-native-test.log'
$receiptPath = 'ci-results/license-return.json'
$activationPattern = '(?m)^Activation complete\.\s*$|^Status:\s*\[200\]\s*ASSIGN_SEAT\s*$'
$returnPattern = 'Successfully returned (the entitlement|ULF|floating) license|License has been returned'
$text = if (Test-Path $nativeLog) { Get-Content $nativeLog -Raw } else { '' }
$activated = $text -match $activationPattern
$returned = $text -match $returnPattern
$returnSource = 'gameci'
$returnExitCode = $null
$returnLogAvailable = Test-Path $nativeLog
$returnDiagnostics = @()
$fallbackAttempted = $false

if ($activated -and (Test-Path $receiptPath)) {
    $previous = Get-Content $receiptPath -Raw | ConvertFrom-Json
    if ($previous.confirmed -eq $true) {
        Write-Host 'The Personal seat return was already confirmed; no duplicate return.'
        exit 0
    }
}
if (-not $activated) {
    # A nonzero activation command is not proof that a seat was allocated.
    # In particular, a rejected login must not start another credential command.
    $returnSource = 'none'
    $returnDiagnostics = @('activation-not-confirmed')
    Write-Host 'No confirmed activation evidence; no return command was run.'
} elseif (-not $returned) {
    $fallbackAttempted = $true
    $returnSource = 'editor-fallback'
    $scratch = Join-Path $env:RUNNER_TEMP 'unity-return'
    New-Item -ItemType Directory $scratch -Force | Out-Null
    $returnLog = Join-Path $scratch 'return.log'
    $editor = Join-Path $env:UNITY_PATH 'Editor/Unity.exe'
    $launcher = (Resolve-Path '.github/ci/wait_unity_windows.ps1').Path
    & pwsh -NoProfile -NonInteractive -File $launcher -TimeoutSeconds 120 $editor -logFile $returnLog -quit -batchmode -returnlicense -username $env:UNITY_EMAIL -password $env:UNITY_PASSWORD -projectPath $scratch | Out-Host
    $returnExitCode = $LASTEXITCODE
    $returnLogAvailable = Test-Path $returnLog
    $returnText = if ($returnLogAvailable) { Get-Content $returnLog -Raw } else { '' }
    $returned = $returnText -match $returnPattern
    $patterns = [ordered]@{
        'missing-ulf' = 'Ulf license file not found'
        'access-token-unavailable' = 'Access token is unavailable'
        'machine-bindings-mismatch' = "Machine bindings don't match"
        'license-client-unavailable' = 'Connection attempt to the License Client.*failed|Failed to acquire global mutex'
        'no-active-license' = 'No license activation found|No valid Unity Editor license found|License is not active'
    }
    $returnDiagnostics = @($patterns.Keys | Where-Object { $returnText -match $patterns[$_] })
    if ($returnExitCode -eq 124) { $returnDiagnostics += 'editor-return-timeout' }
}
New-Item -ItemType Directory 'ci-results' -Force | Out-Null
@{
    activationObserved = $activated
    fallbackAttempted = $fallbackAttempted
    confirmed = if ($activated) { $returned } else { $null }
    platform = 'WindowsEditor'
    source = $returnSource
    exitCode = $returnExitCode
    logAvailable = $returnLogAvailable
    diagnostics = $returnDiagnostics
} | ConvertTo-Json | Set-Content $receiptPath
if ($activated) {
    Write-Host "Personal seat return confirmed: $returned (Editor exit: $returnExitCode)."
    if (-not $returned) { throw 'The activated Personal seat was not confirmed returned.' }
}
