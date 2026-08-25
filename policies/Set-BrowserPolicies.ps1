<#
    Configures Chrome and Edge for Screen Time Guardian.

    Three extension policy modes (default is PermissionBased):

      PermissionBased – Guardian is force-installed; other extensions are allowed
        but blocked from requesting dangerous permissions (proxy, VPN, debugging,
        web-request interception, management, etc.). Does NOT set
        BlockExternalExtensions, so local unpacked extensions can still be tested.

      Allowlist – all extensions are blocked by default. Only Guardian and the
        extensions listed in -AllowedExtensionIds are permitted. Also sets
        BlockExternalExtensions = 1.

      Strict – all extensions except Guardian are blocked. Also sets
        BlockExternalExtensions = 1.

    Private and guest browsing are controlled dynamically by the service while a
    block is active. This script does NOT set IncognitoModeAvailability or
    BrowserGuestModeEnabled — the service owns those values.

    Developer tools are disabled unless -AllowDeveloperTools is passed.

    All values are written under HKLM, so they apply to every user on the machine.
    During removal, only values known to be owned by Guardian are deleted; policy
    written by other software is preserved.

    Examples:

      .\Set-BrowserPolicies.ps1 `
          -ExtensionId aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa `
          -EdgeExtensionId bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb `
          -UpdateUrl https://example.com/chrome/update.xml `
          -EdgeUpdateUrl https://example.com/edge/update.xml

      .\Set-BrowserPolicies.ps1 ... -ExtensionPolicy Allowlist `
          -AllowedExtensionIds cccccccccccccccccccccccccccccccc,dddddddddddddddddddddddddddddddd

      .\Set-BrowserPolicies.ps1 ... -ExtensionPolicy Strict -Remove
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId,

    [string]$UpdateUrl,

    [string]$EdgeExtensionId,

    [string]$EdgeUpdateUrl,

    [ValidateSet('PermissionBased', 'Allowlist', 'Strict')]
    [string]$ExtensionPolicy = 'PermissionBased',

    [string[]]$AllowedExtensionIds = @(),

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

# ------------------------------------------------------------------ validation
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
        if (-not [Uri]::TryCreate($url, [UriKind]::Absolute, [ref]$parsed) -or
            $parsed.Scheme -ne 'https' -or
            -not [string]::IsNullOrWhiteSpace($parsed.UserInfo) -or
            $parsed.Port -ne -1 -or
            -not [string]::IsNullOrWhiteSpace($parsed.Query) -or
            -not [string]::IsNullOrWhiteSpace($parsed.Fragment)) {
            throw "כתובת העדכון חייבת להיות HTTPS ללא פרטי משתמש, port, query או fragment: $url"
        }
    }

    if ($ExtensionPolicy -eq 'Allowlist' -and $AllowedExtensionIds.Count -gt 0) {
        foreach ($id in $AllowedExtensionIds) {
            if ($id -notmatch '^[a-z]{32}$') {
                throw "מזהה תוסף באישור חייב להיות 32 אותיות קטנות: $id"
            }
        }
    }
}

$browsers = @(
    @{ Name = 'Chrome'; Root = 'HKLM:\SOFTWARE\Policies\Google\Chrome'; Id = $ExtensionId;     Update = $UpdateUrl }
    @{ Name = 'Edge';   Root = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge'; Id = $EdgeExtensionId; Update = $EdgeUpdateUrl }
)

$blockedPermissions = @(
    'proxy'
    'webRequest'
    'webRequestBlocking'
    'declarativeNetRequest'
    'declarativeNetRequestWithHostAccess'
    'management'
    'debugger'
    'privacy'
    'vpnProvider'
)

# --------------------------------------------------------- helper functions
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
        return $wildcard.installation_mode -eq 'blocked' -and
            $guardian.installation_mode -eq 'force_installed' -and
            ([string]::IsNullOrWhiteSpace([string]$Browser.Update) -or
                $guardian.update_url -eq $Browser.Update)
    }
    catch {
        return $false
    }
}

# --------------------------------------------------------------- remove
function Remove-BrowserPolicy {
    param($Browser)

    if ([string]::IsNullOrWhiteSpace([string]$Browser.Id)) {
        return
    }

    if (-not (Test-Path $Browser.Root)) {
        Write-Host "  מדיניות $($Browser.Name) לא נמצאה." -ForegroundColor Yellow
        return
    }

    # -- ExtensionInstallForcelist --------------------------------------------
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

    # -- values owned by Guardian ---------------------------------------------
    $marker = [string](Get-ItemProperty -Path $Browser.Root -Name 'STGManagedPolicyVersion' -ErrorAction SilentlyContinue).STGManagedPolicyVersion
    $legacyOwned = Test-LegacyGuardianExtensionSettings -Browser $Browser

    if ($marker -eq '1' -or $marker -eq '2' -or $legacyOwned) {
        Remove-ItemProperty -Path $Browser.Root -Name 'ExtensionSettings' -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $Browser.Root -Name 'BlockExternalExtensions' -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $Browser.Root -Name 'STGManagedPolicyVersion' -ErrorAction SilentlyContinue
    }

    # -- DeveloperToolsAvailability (restore previous if saved) ----------------
    $previousDevTools = (Get-ItemProperty -Path $Browser.Root -Name 'STGPreviousDeveloperToolsAvailability' -ErrorAction SilentlyContinue).STGPreviousDeveloperToolsAvailability
    if ($null -ne $previousDevTools) {
        New-ItemProperty -Path $Browser.Root -Name 'DeveloperToolsAvailability' `
            -Value ([int]$previousDevTools) -PropertyType DWord -Force | Out-Null
        Remove-ItemProperty -Path $Browser.Root -Name 'STGPreviousDeveloperToolsAvailability' -ErrorAction SilentlyContinue
    }
    elseif ($marker -eq '1' -or $marker -eq '2' -or $legacyOwned) {
        Remove-ItemProperty -Path $Browser.Root -Name 'DeveloperToolsAvailability' -ErrorAction SilentlyContinue
    }

    Write-Host "  מדיניות Guardian של $($Browser.Name) הוסרה; מדיניות חיצונית נשמרה." -ForegroundColor Yellow
}

# --------------------------------------------------------------- apply
function Set-BrowserPolicy {
    param($Browser)

    New-Item -Path $Browser.Root -Force | Out-Null

    # -- migrate from legacy wildcard blocking ---------------------------------
    if (Test-LegacyGuardianExtensionSettings -Browser $Browser) {
        Remove-ItemProperty -Path $Browser.Root -Name 'ExtensionSettings' -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $Browser.Root -Name 'BlockExternalExtensions' -ErrorAction SilentlyContinue
    }

    # -- force-install Guardian ------------------------------------------------
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

    # -- extension policy (mode-specific) --------------------------------------
    $extensionSettings = @{}

    # Guardian entry (needed to override wildcard in PermissionBased mode)
    $guardianEntry = @{
        installation_mode = 'force_installed'
        update_url        = $Browser.Update
    }

    switch ($ExtensionPolicy) {
        'PermissionBased' {
            $extensionSettings['*'] = @{ blocked_permissions = $blockedPermissions }
            $extensionSettings[$Browser.Id] = $guardianEntry

            Remove-ItemProperty -Path $Browser.Root -Name 'BlockExternalExtensions' -ErrorAction SilentlyContinue
        }

        'Allowlist' {
            $extensionSettings['*'] = @{ installation_mode = 'blocked' }
            $extensionSettings[$Browser.Id] = $guardianEntry

            # BlockExternalExtensions prevents loading unpacked extensions that
            # are not in the ExtensionInstallForcelist.
            New-ItemProperty -Path $Browser.Root -Name 'BlockExternalExtensions' `
                -Value 1 -PropertyType DWord -Force | Out-Null

            foreach ($id in $AllowedExtensionIds) {
                if ($id -ne $Browser.Id) {
                    $extensionSettings[$id] = @{ installation_mode = 'allowed' }
                }
            }
        }

        'Strict' {
            $extensionSettings['*'] = @{ installation_mode = 'blocked' }
            $extensionSettings[$Browser.Id] = $guardianEntry

            New-ItemProperty -Path $Browser.Root -Name 'BlockExternalExtensions' `
                -Value 1 -PropertyType DWord -Force | Out-Null
        }
    }

    # Write as JSON string to the registry
    $json = $extensionSettings | ConvertTo-Json -Depth 5
    New-ItemProperty -Path $Browser.Root -Name 'ExtensionSettings' `
        -Value $json -PropertyType String -Force | Out-Null

    # -- ownership marker ------------------------------------------------------
    New-ItemProperty -Path $Browser.Root -Name 'STGManagedPolicyVersion' `
        -Value '2' -PropertyType String -Force | Out-Null

    # -- DeveloperToolsAvailability --------------------------------------------
    $existingDevTools = (Get-ItemProperty -Path $Browser.Root -ErrorAction SilentlyContinue).PSObject.Properties['DeveloperToolsAvailability']
    $previousDevTools = (Get-ItemProperty -Path $Browser.Root -ErrorAction SilentlyContinue).PSObject.Properties['STGPreviousDeveloperToolsAvailability']
    if ($null -ne $existingDevTools -and $null -eq $previousDevTools) {
        New-ItemProperty -Path $Browser.Root -Name 'STGPreviousDeveloperToolsAvailability' `
            -Value ([int]$existingDevTools.Value) -PropertyType DWord -Force | Out-Null
    }

    $devTools = if ($AllowDeveloperTools) { 1 } else { 2 }
    New-ItemProperty -Path $Browser.Root -Name 'DeveloperToolsAvailability' `
        -Value $devTools -PropertyType DWord -Force | Out-Null

    Write-Host "  מדיניות $($Browser.Name) הוחלה — $ExtensionPolicy." -ForegroundColor Green
}

# ====================================================================== main
Write-Host ''
foreach ($browser in $browsers) {
    Write-Host "$($browser.Name):" -ForegroundColor Cyan
    if ($Remove) { Remove-BrowserPolicy -Browser $browser } else { Set-BrowserPolicy -Browser $browser }
}

Write-Host ''
if (-not $Remove) {
    Write-Host 'סגור ופתח מחדש את הדפדפנים כדי שהמדיניות תיכנס לתוקף.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "מצב מדיניות: $ExtensionPolicy" -ForegroundColor Cyan
    switch ($ExtensionPolicy) {
        'PermissionBased' {
            Write-Host '  Guardian כפוי. תוספים אחרים מותרים, אבל הרשאות מסוכנות חסומות.'
            Write-Host '  (proxy, webRequest, DNR, management, debugger, privacy, VPN)'
        }
        'Allowlist' {
            $extra = if ($AllowedExtensionIds.Count -gt 0) { " + $($AllowedExtensionIds.Count) מאושרים" } else { '' }
            Write-Host "  Guardian כפוי. כל שאר התוספים חסומים${extra}."
        }
        'Strict' {
            Write-Host '  Guardian כפוי. כל שאר התוספים חסומים.'
        }
    }
    Write-Host ''
    Write-Host 'גלישה פרטית ומצב אורח מנוהלים דינמית על ידי השירות — נסגרים רק כשחסימה פעילה.' -ForegroundColor Cyan
    Write-Host 'לבדיקה: chrome://policy  או  edge://policy — לחץ Reload policies וודא שאין שגיאות.'
    Write-Host ''
    Write-Host 'תוספים אחרים: מדיניות זו מונעת התקנת דפדפן אחר' -ForegroundColor Green
    Write-Host 'רק כאשר מופעלת "חסימת הפעלה של דפדפנים לא מאושרים" בלוח הבקרה.'
}