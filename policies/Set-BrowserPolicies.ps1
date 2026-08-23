<#
    Configures Chrome and Edge for Screen Time Guardian.

    What this achieves:
      * the Screen Time Guardian extension is force installed;
      * other browser extensions remain allowed;
      * private and guest browsing are controlled dynamically while a block is active;
      * developer tools are disabled unless explicitly allowed;
      * these settings apply to every user account on the machine.

    This script deliberately does NOT set ExtensionSettings with a wildcard block and
    does NOT set BlockExternalExtensions. Existing policies owned by other software are
    preserved during both apply and removal.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId,

    # Explicit update URLs are required when applying policy. They are not needed
    # when -Remove is used.
    [string]$UpdateUrl,

    [string]$EdgeExtensionId,

    [string]$EdgeUpdateUrl,

    [switch]$AllowDeveloperTools,
    [switch]$Remove
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'יש להריץ סקריפט זה מחלון PowerShell עם הרשאות מנהל.'
}

if ($Remove) {
    if ($ExtensionId -notmatch '^[a-z]{32}$' -or ($EdgeExtensionId -and $EdgeExtensionId -notmatch '^[a-z]{32}$')) {
        throw 'בעת הסרה יש לספק מזהי תוסף תקינים.'
    }
}
else {
    if ($ExtensionId -notmatch '^[a-z]{32}$' -or $EdgeExtensionId -notmatch '^[a-z]{32}$') {
        throw 'בעת החלה יש לספק מזהי Chrome ו־Edge תקינים בני 32 אותיות קטנות.'
    }

    foreach ($url in @($UpdateUrl, $EdgeUpdateUrl)) {
        $parsed = $null
        if (-not [Uri]::TryCreate($url, [UriKind]::Absolute, [ref]$parsed)
            -or $parsed.Scheme -ne 'https'
            -or -not [string]::IsNullOrWhiteSpace($parsed.UserInfo)
            -or $parsed.Port -ne -1
            -or -not [string]::IsNullOrWhiteSpace($parsed.Query)
            -or -not [string]::IsNullOrWhiteSpace($parsed.Fragment)) {
            throw "כתובת העדכון חייבת להיות HTTPS ללא פרטי משתמש, port, query או fragment: $url"
        }
    }
}

$browsers = @(
    @{ Name = 'Chrome'; Root = 'HKLM:\SOFTWARE\Policies\Google\Chrome'; Id = $ExtensionId;     Update = $UpdateUrl }
    @{ Name = 'Edge';   Root = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Id = $EdgeExtensionId; Update = $EdgeUpdateUrl }
)

function Get-ValueNames {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return @()
    }

    return @((Get-ItemProperty -Path $Path).PSObject.Properties.Name |
        Where-Object { $_ -notlike 'PS*' })
}

function Test-LegacyGuardianExtensionSettings {
    param($Browser)

    $root = $Browser.Root
    if (-not (Test-Path $root)) {
        return $false
    }

    $raw = (Get-ItemProperty -Path $root -Name 'ExtensionSettings' -ErrorAction SilentlyContinue).ExtensionSettings
    if ([string]::IsNullOrWhiteSpace([string]$raw)) {
        return $false
    }

    try {
        $settings = $raw | ConvertFrom-Json
        $names = @($settings.PSObject.Properties.Name)
        if ($names.Count -ne 2 -or $names -notcontains '*' -or $names -notcontains $Browser.Id) {
            return $false
        }

        $wildcard = $settings.PSObject.Properties['*'].Value
        $guardian = $settings.PSObject.Properties[$Browser.Id].Value
        return $wildcard.installation_mode -eq 'blocked'
            -and $guardian.installation_mode -eq 'force_installed'
            -and ([string]::IsNullOrWhiteSpace([string]$Browser.Update)
                -or $guardian.update_url -eq $Browser.Update)
    }
    catch {
        return $false
    }
}

function Remove-BrowserPolicy {
    param($Browser)

    if ([string]::IsNullOrWhiteSpace([string]$Browser.Id)) {
        return
    }

    if (-not (Test-Path $Browser.Root)) {
        Write-Host "  מדיניות $($Browser.Name) לא נמצאה." -ForegroundColor Yellow
        return
    }

    $forceList = Join-Path $Browser.Root 'ExtensionInstallForcelist'
    if (Test-Path $forceList) {
        foreach ($name in Get-ValueNames -Path $forceList) {
            $value = [string](Get-ItemProperty -Path $forceList -Name $name).$name
            if ($value -match "^$([regex]::Escape($Browser.Id));") {
                Remove-ItemProperty -Path $forceList -Name $name -Force
            }
        }

        if ((Get-ValueNames -Path $forceList).Count -eq 0) {
            Remove-Item -Path $forceList -Recurse -Force
        }
    }

    $marker = [string](Get-ItemProperty -Path $Browser.Root -Name 'STGManagedPolicyVersion' -ErrorAction SilentlyContinue).STGManagedPolicyVersion
    $legacyOwned = Test-LegacyGuardianExtensionSettings -Browser $Browser
    if ($marker -eq '1' -or $legacyOwned) {
        foreach ($valueName in @('ExtensionSettings', 'BlockExternalExtensions', 'STGManagedPolicyVersion')) {
            Remove-ItemProperty -Path $Browser.Root -Name $valueName -ErrorAction SilentlyContinue
        }

        $previousDevTools = (Get-ItemProperty -Path $Browser.Root -Name 'STGPreviousDeveloperToolsAvailability' -ErrorAction SilentlyContinue).STGPreviousDeveloperToolsAvailability
        if ($null -ne $previousDevTools) {
            New-ItemProperty -Path $Browser.Root -Name 'DeveloperToolsAvailability' `
                -Value ([int]$previousDevTools) -PropertyType DWord -Force | Out-Null
            Remove-ItemProperty -Path $Browser.Root -Name 'STGPreviousDeveloperToolsAvailability' -ErrorAction SilentlyContinue
        }
        else {
            Remove-ItemProperty -Path $Browser.Root -Name 'DeveloperToolsAvailability' -ErrorAction SilentlyContinue
        }
    }

    Write-Host "  מדיניות Guardian של $($Browser.Name) הוסרה; מדיניות חיצונית נשמרה." -ForegroundColor Yellow
}

function Set-BrowserPolicy {
    param($Browser)

    New-Item -Path $Browser.Root -Force | Out-Null

    # Upgrade from the old Guardian release if its exact two-entry wildcard policy
    # is still present. Never remove an unrelated ExtensionSettings document.
    if (Test-LegacyGuardianExtensionSettings -Browser $Browser) {
        Remove-ItemProperty -Path $Browser.Root -Name 'ExtensionSettings' -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $Browser.Root -Name 'BlockExternalExtensions' -ErrorAction SilentlyContinue
    }

    # --- force install our extension without touching other extensions ------------
    $forceList = Join-Path $Browser.Root 'ExtensionInstallForcelist'
    New-Item -Path $forceList -Force | Out-Null
    $desiredValue = "$($Browser.Id);$($Browser.Update)"
    $matchingNames = @()

    foreach ($name in Get-ValueNames -Path $forceList) {
        $value = [string](Get-ItemProperty -Path $forceList -Name $name).$name
        if ($value -match "^$([regex]::Escape($Browser.Id));") {
            $matchingNames += $name
        }
    }

    if ($matchingNames.Count -eq 0) {
        $numericNames = @(Get-ValueNames -Path $forceList | Where-Object { $_ -match '^\d+$' })
        $next = 1
        if ($numericNames.Count -gt 0) {
            $next = ([int]($numericNames | Measure-Object -Maximum).Maximum) + 1
        }
        $matchingNames = @([string]$next)
    }

    Set-ItemProperty -Path $forceList -Name $matchingNames[0] -Value $desiredValue
    foreach ($duplicate in $matchingNames | Select-Object -Skip 1) {
        Remove-ItemProperty -Path $forceList -Name $duplicate -Force
    }

    # Do not set ExtensionSettings wildcard blocking or BlockExternalExtensions.
    # Other extensions must remain installable and usable.
    New-ItemProperty -Path $Browser.Root -Name 'STGManagedPolicyVersion' `
        -Value '1' -PropertyType String -Force | Out-Null

    # These values are still controlled dynamically by the service while a block is active.
    # Setting them here would fight the service for the same registry values.

    $existingDevTools = (Get-ItemProperty -Path $Browser.Root -ErrorAction SilentlyContinue).PSObject.Properties['DeveloperToolsAvailability']
    $previousDevTools = (Get-ItemProperty -Path $Browser.Root -ErrorAction SilentlyContinue).PSObject.Properties['STGPreviousDeveloperToolsAvailability']
    if ($null -ne $existingDevTools -and $null -eq $previousDevTools) {
        New-ItemProperty -Path $Browser.Root -Name 'STGPreviousDeveloperToolsAvailability' `
            -Value ([int]$existingDevTools.Value) -PropertyType DWord -Force | Out-Null
    }

    $devTools = if ($AllowDeveloperTools) { 1 } else { 2 }  # 2 = disallowed
    New-ItemProperty -Path $Browser.Root -Name 'DeveloperToolsAvailability' `
        -Value $devTools -PropertyType DWord -Force | Out-Null

    Write-Host "  מדיניות $($Browser.Name) הוחלה. התוסף של Guardian כפוי; תוספים אחרים מותרים." -ForegroundColor Green
}

Write-Host ''
foreach ($browser in $browsers) {
    Write-Host "$($browser.Name):" -ForegroundColor Cyan
    if ($Remove) { Remove-BrowserPolicy -Browser $browser } else { Set-BrowserPolicy -Browser $browser }
}

Write-Host ''
if (-not $Remove) {
    Write-Host 'סגור ופתח מחדש את הדפדפנים כדי שהמדיניות תיכנס לתוקף.' -ForegroundColor Yellow
    Write-Host 'גלישה פרטית ומצב אורח מנוהלים דינמית על ידי השירות — נסגרים רק כשחסימה פעילה.' -ForegroundColor Cyan
    Write-Host 'לבדיקה: chrome://policy  או  edge://policy — לחץ Reload policies וודא שאין שגיאות.'
    Write-Host ''
    Write-Host 'תוספים אחרים מותרים. מדיניות זו אינה מונעת התקנת דפדפן אחר.' -ForegroundColor Green
    Write-Host 'לשם כך יש להפעיל "חסימת הפעלה של דפדפנים לא מאושרים" בלוח הבקרה.'
}
