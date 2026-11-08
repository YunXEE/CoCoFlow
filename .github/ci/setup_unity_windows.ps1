param([ValidateSet('mono', 'il2cpp')][string]$PlayerBackend = 'mono')
$ErrorActionPreference = 'Stop'
$config = Get-Content '.github/ci/unity-host.json' -Raw | ConvertFrom-Json
$unityRoot = Join-Path $env:RUNNER_TEMP "Unity-$($config.unity)"
$installer = Join-Path ([IO.Path]::GetTempPath()) "UnitySetup64-$($config.unity).exe"

# Fail fast on release-asset transport errors, before the large Editor install.
$gameci = Join-Path $env:RUNNER_TEMP 'game-ci.exe'
Invoke-WebRequest -Uri $config.gameci.url -OutFile $gameci -MaximumRetryCount 2 -RetryIntervalSec 5
if ((Get-FileHash $gameci -Algorithm SHA256).Hash -ne $config.gameci.sha256) {
    throw 'GameCI executable SHA256 does not match the official release.'
}

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

$toolchain = $null
$moduleReceipt = $null
if ($PlayerBackend -eq 'il2cpp') {
    $moduleInstaller = Join-Path $env:RUNNER_TEMP "UnitySetup-Windows-IL2CPP-$($config.unity).exe"
    Invoke-WebRequest -Uri $config.windowsIl2CppInstaller.url -OutFile $moduleInstaller -MaximumRetryCount 2 -RetryIntervalSec 5
    $moduleIntegrity = 'md5-' + [Convert]::ToBase64String(
        [Convert]::FromHexString((Get-FileHash $moduleInstaller -Algorithm MD5).Hash))
    if ($moduleIntegrity -ne $config.windowsIl2CppInstaller.integrity) {
        throw 'Windows IL2CPP module integrity does not match the official release.'
    }
    if ((Get-AuthenticodeSignature $moduleInstaller).Status -ne 'Valid') {
        throw 'Windows IL2CPP module signature is not valid.'
    }
    $moduleProcess = Start-Process $moduleInstaller -ArgumentList @('/S', "/D=$unityRoot") -Wait -PassThru
    if ($moduleProcess.ExitCode -ne 0) { throw "Windows IL2CPP module installer exit: $($moduleProcess.ExitCode)" }
    Remove-Item $moduleInstaller
    $variationsPath = Join-Path $unityRoot 'Editor/Data/PlaybackEngines/windowsstandalonesupport/Variations'
    $variants = @(Get-ChildItem $variationsPath -Directory | Where-Object Name -Match 'il2cpp' | Select-Object -ExpandProperty Name)
    if ($variants.Count -eq 0) { throw 'Installed Windows IL2CPP Player variations were not found.' }

    # Hosted Windows images normally supply these; verify reality before activation.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or -not $visualStudio) { throw 'Visual C++ x64/x86 tools were not found.' }
    $compiler = Get-ChildItem (Join-Path $visualStudio 'VC/Tools/MSVC') -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'bin/Hostx64/x64/cl.exe' } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $compiler) { throw 'The detected Visual Studio installation has no x64 compiler.' }
    $sdkRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
    $sdk = Get-ChildItem (Join-Path $sdkRoot 'Lib') -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and [version]$_.Name -ge [version]'10.0.19041.0' } |
        Where-Object { Test-Path (Join-Path $_.FullName 'um/x64/kernel32.lib') } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $sdk) { throw 'Windows SDK 10.0.19041.0 or newer was not found.' }
    $toolchain = @{ visualStudio = $visualStudio; compiler = $compiler; sdk = $sdk.Name; sdkRoot = $sdkRoot }
    $moduleReceipt = @{ installer = $config.windowsIl2CppInstaller; signature = 'Valid'; variants = $variants }
}
"UNITY_PATH=$unityRoot" >> $env:GITHUB_ENV
"COCOFLOW_GAMECI_EXE=$gameci" >> $env:GITHUB_ENV
New-Item -ItemType Directory 'ci-results' -Force | Out-Null
@{
    unity = $config.unity
    editorInstaller = $config.editorInstaller
    gameci = $config.gameci
    signature = 'Valid'
    playerBackend = $PlayerBackend
    il2cppModule = $moduleReceipt
    toolchain = $toolchain
} | ConvertTo-Json -Depth 8 | Set-Content 'ci-results/installation.json'
