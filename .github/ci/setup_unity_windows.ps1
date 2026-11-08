$ErrorActionPreference = 'Stop'
$config = Get-Content '.github/ci/unity-host.json' -Raw | ConvertFrom-Json
$unityRoot = Join-Path $env:RUNNER_TEMP "Unity-$($config.unity)"
$installer = Join-Path ([IO.Path]::GetTempPath()) "UnitySetup64-$($config.unity).exe"

# Official NSIS install: /D must be the final, unquoted argument.
Invoke-WebRequest -Uri $config.editorInstaller.url -OutFile $installer
$actual = 'md5-' + [Convert]::ToBase64String(
    [Convert]::FromHexString((Get-FileHash $installer -Algorithm MD5).Hash))
if ($actual -ne $config.editorInstaller.integrity) {
    throw 'Unity installer integrity does not match the official release.'
}
if ((Get-AuthenticodeSignature $installer).Status -ne 'Valid') {
    throw 'Unity installer signature is not valid.'
}
$process = Start-Process $installer -ArgumentList @('/S', "/D=$unityRoot") -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Unity installer exit code: $($process.ExitCode)" }
Remove-Item $installer
if (-not (Test-Path (Join-Path $unityRoot 'Editor/Unity.exe'))) {
    throw 'The installed Unity Editor was not found.'
}

$gameci = Join-Path $env:RUNNER_TEMP 'game-ci.exe'
# Only the pinned asset download retries; activation and test attempts remain one.
Invoke-WebRequest -Uri $config.gameci.url -OutFile $gameci -MaximumRetryCount 2 -RetryIntervalSec 5
if ((Get-FileHash $gameci -Algorithm SHA256).Hash -ne $config.gameci.sha256) {
    throw 'GameCI executable SHA256 does not match the official release.'
}
"UNITY_PATH=$unityRoot" >> $env:GITHUB_ENV
"COCOFLOW_GAMECI_EXE=$gameci" >> $env:GITHUB_ENV
New-Item -ItemType Directory 'ci-results' -Force | Out-Null
@{
    unity = $config.unity
    editorInstaller = $config.editorInstaller
    gameci = $config.gameci
    signature = 'Valid'
} | ConvertTo-Json -Depth 5 | Set-Content 'ci-results/installation.json'
