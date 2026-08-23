param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z]{32}$')]
    [string]$ExtensionId,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z]{32}$')]
    [string]$EdgeExtensionId,
    [string]$NativeHostPath = 'C:\Program Files\ScreenTimeGuardian\NativeHost\ScreenTimeGuardian.NativeHost.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ExtensionId -notmatch '^[a-z]{32}$') {
    throw 'ExtensionId must be the final 32-character Chrome extension id.'
}

if (-not (Test-Path -LiteralPath $NativeHostPath -PathType Leaf)) {
    throw "Native host executable was not found: $NativeHostPath"
}

$resolvedNativeHostPath = (Resolve-Path -LiteralPath $NativeHostPath).Path
$manifest = @{
    name = 'com.screentimeguardian.host'
    description = 'Screen Time Guardian policy bridge'
    path = $resolvedNativeHostPath
    type = 'stdio'
    allowed_origins = @(
        "chrome-extension://$ExtensionId/"
        "chrome-extension://$EdgeExtensionId/"
    )
} | ConvertTo-Json -Depth 4

$manifestPath = Join-Path $env:ProgramData 'ScreenTimeGuardian\com.screentimeguardian.host.json'
New-Item -ItemType Directory -Force -Path (Split-Path $manifestPath) | Out-Null
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifest,
    [System.Text.UTF8Encoding]::new($false))

$chromeKey = 'HKLM:\Software\Google\Chrome\NativeMessagingHosts\com.screentimeguardian.host'
$edgeKey = 'HKLM:\Software\Microsoft\Edge\NativeMessagingHosts\com.screentimeguardian.host'
New-Item -Path $chromeKey -Force | Out-Null
New-Item -Path $edgeKey -Force | Out-Null
Set-ItemProperty -Path $chromeKey -Name '(default)' -Value $manifestPath
Set-ItemProperty -Path $edgeKey -Name '(default)' -Value $manifestPath

Write-Host 'Native Messaging host registered for Chrome and Edge.'
Write-Host 'Restart Chrome and Edge to load the registration.'
