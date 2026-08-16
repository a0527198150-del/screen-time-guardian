param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId,
    [ValidateSet('AllowGuest', 'BlockGuest')]
    [string]$GuestMode = 'AllowGuest'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ExtensionId -notmatch '^[a-z]{32}$') {
    throw 'ExtensionId must be the final 32-character Chrome extension id.'
}

$guestValue = if ($GuestMode -eq 'AllowGuest') { 1 } else { 0 }
$chrome = 'HKLM:\Software\Policies\Google\Chrome'
$edge = 'HKLM:\Software\Policies\Microsoft\Edge'

foreach ($key in @($chrome, $edge)) {
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name 'BrowserGuestModeEnabled' -PropertyType DWord -Value $guestValue -Force | Out-Null
    # 0 means Incognito is available. It must remain available only when the
    # managed extension is force-installed and allowed in Incognito.
    New-ItemProperty -Path $key -Name 'IncognitoModeAvailability' -PropertyType DWord -Value 0 -Force | Out-Null
}

# The final ExtensionSettings value must be produced with the real deployment
# URL. A local unpacked extension cannot be force-installed in production by
# this template alone.
$settingsObject = [ordered]@{}
$settingsObject['*'] = [ordered]@{
    installation_mode = 'blocked'
}
$settingsObject[$ExtensionId] = [ordered]@{
    installation_mode = 'force_installed'
    update_url = 'https://clients2.google.com/service/update2/crx'
}
$settings = $settingsObject | ConvertTo-Json -Compress -Depth 8

foreach ($key in @($chrome, $edge)) {
    New-ItemProperty -Path $key -Name 'ExtensionSettings' -PropertyType String -Value $settings -Force | Out-Null
}

Write-Host 'Browser policies written. Restart Chrome and Edge for policy changes to take effect.'
