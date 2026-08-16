param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId,
    [string]$NativeHostPath = 'C:\Program Files\ScreenTimeGuardian\ScreenTimeGuardian.NativeHost.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ExtensionId -notmatch '^[a-z]{32}$') {
    throw 'ExtensionId must be the final 32-character Chrome extension id.'
}

$manifest = @{
    name = 'com.screentimeguardian.host'
    description = 'Screen Time Guardian policy bridge'
    path = $NativeHostPath
    type = 'stdio'
    allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4

$manifestPath = Join-Path $env:ProgramData 'ScreenTimeGuardian\com.screentimeguardian.host.json'
New-Item -ItemType Directory -Force -Path (Split-Path $manifestPath) | Out-Null
Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8

$chromeKey = 'HKLM:\Software\Google\Chrome\NativeMessagingHosts\com.screentimeguardian.host'
$edgeKey = 'HKLM:\Software\Microsoft\Edge\NativeMessagingHosts\com.screentimeguardian.host'
New-Item -Path $chromeKey -Force | Out-Null
New-Item -Path $edgeKey -Force | Out-Null
Set-ItemProperty -Path $chromeKey -Name '(default)' -Value $manifestPath
Set-ItemProperty -Path $edgeKey -Name '(default)' -Value $manifestPath

Write-Host 'Native Messaging host registered. Restart Chrome and Edge to load the registration.'
