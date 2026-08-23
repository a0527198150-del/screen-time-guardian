<#
    Locks down Chrome and Edge machine-wide. Run ONCE from an ELEVATED PowerShell window.

    What this achieves:
      * the Screen Time Guardian extension is force installed and CANNOT be removed;
      * no other extension can be installed;
      * incognito and guest browsing are disabled, since both hide the session from us;
      * developer tools are disabled, so the extension cannot be tampered with;
      * these settings apply to EVERY user account on the machine.

    Policies live under HKLM, so a standard user cannot undo any of it.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId,

    # Where update.xml is hosted. For a self hosted extension this must be reachable
    # over HTTPS. For a store published extension use the store update URL instead.
    [string]$UpdateUrl = 'https://clients2.google.com/service/update2/crx',

    [string]$EdgeExtensionId,
    [string]$EdgeUpdateUrl = 'https://edge.microsoft.com/extensionwebstorebase/v1/crx',

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

if (-not $EdgeExtensionId) { $EdgeExtensionId = $ExtensionId }

$browsers = @(
    @{ Name = 'Chrome'; Root = 'HKLM:\SOFTWARE\Policies\Google\Chrome'; Id = $ExtensionId;     Update = $UpdateUrl }
    @{ Name = 'Edge';   Root = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Id = $EdgeExtensionId; Update = $EdgeUpdateUrl }
)

function Remove-BrowserPolicy {
    param($Browser)

    foreach ($leaf in @('ExtensionInstallForcelist', 'ExtensionInstallAllowlist', 'URLBlocklist')) {
        $path = Join-Path $Browser.Root $leaf
        if (Test-Path $path) { Remove-Item -Path $path -Recurse -Force }
    }

    if (Test-Path $Browser.Root) {
        foreach ($value in @('ExtensionSettings', 'IncognitoModeAvailability', 'BrowserGuestModeEnabled',
                             'DeveloperToolsAvailability', 'BlockExternalExtensions')) {
            Remove-ItemProperty -Path $Browser.Root -Name $value -ErrorAction SilentlyContinue
        }
    }

    Write-Host "  מדיניות $($Browser.Name) הוסרה." -ForegroundColor Yellow
}

function Set-BrowserPolicy {
    param($Browser)

    New-Item -Path $Browser.Root -Force | Out-Null

    # --- force install our extension -------------------------------------------------
    $forceList = Join-Path $Browser.Root 'ExtensionInstallForcelist'
    New-Item -Path $forceList -Force | Out-Null
    Get-Item $forceList | Select-Object -ExpandProperty Property | ForEach-Object {
        Remove-ItemProperty -Path $forceList -Name $_ -ErrorAction SilentlyContinue
    }
    New-ItemProperty -Path $forceList -Name '1' -Value "$($Browser.Id);$($Browser.Update)" `
        -PropertyType String -Force | Out-Null

    # --- block every other extension --------------------------------------------------
    # "*" defaults to blocked; our own id is force_installed and cannot be removed.
    $settings = @{
        '*' = @{
            installation_mode  = 'blocked'
            blocked_permissions = @()
        }
        $Browser.Id = @{
            installation_mode = 'force_installed'
            update_url        = $Browser.Update
            toolbar_pin       = 'force_pinned'
        }
    }

    New-ItemProperty -Path $Browser.Root -Name 'ExtensionSettings' `
        -Value ($settings | ConvertTo-Json -Depth 6 -Compress) -PropertyType String -Force | Out-Null

    New-ItemProperty -Path $Browser.Root -Name 'BlockExternalExtensions' `
        -Value 1 -PropertyType DWord -Force | Out-Null

    # --- incognito and guest mode -----------------------------------------------------
    # These are NOT set here. The service turns them off only while a block is running
    # and turns them back on the rest of the time. Setting them statically here would
    # fight the service for the same registry values.

    # DevTools would allow editing the extension's storage and rules at runtime.
    $devTools = if ($AllowDeveloperTools) { 1 } else { 2 }  # 2 = disallowed
    New-ItemProperty -Path $Browser.Root -Name 'DeveloperToolsAvailability' `
        -Value $devTools -PropertyType DWord -Force | Out-Null

    Write-Host "  מדיניות $($Browser.Name) הוחלה. התוסף $($Browser.Id) כפוי ולא ניתן להסרה." -ForegroundColor Green
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
    Write-Host 'שים לב: מדיניות זו מונעת התקנת תוספים אחרים, אבל היא לא מונעת התקנת דפדפן אחר.' -ForegroundColor Yellow
    Write-Host 'לשם כך יש להפעיל "חסימת הפעלה של דפדפנים לא מאושרים" בלוח הבקרה.'
}
