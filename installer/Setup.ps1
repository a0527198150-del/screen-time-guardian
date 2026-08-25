<#
    Screen Time Guardian – מתקין מקצועי v0.5.0

    מתקין בסגנון Google: תהליך התקנה מהיר, אוטומטי, עם Creates shortcuts,
    הרשמה אוטומטית של כל הרכיבים, và אפשרות הפעלה בסוף.

    לחיצה כפולה על Install.cmd = התקנה מלאה.
    לחיצה כפולה על Uninstall.cmd = הסרה מלאה.
    לחיצה כפולה על Emergency-Stop.cmd = עצירת כל אכיפה.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceFolder,
    [switch]$Uninstall,
    [switch]$EmergencyStop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SourceFolder = $SourceFolder.TrimEnd('\')

$script:InstallRoot = 'C:\Program Files\ScreenTimeGuardian'
$script:DataDir     = 'C:\ProgramData\ScreenTimeGuardian'
$script:ServiceName = 'ScreenTimeGuardian'
$script:Version     = '0.5.1'
$script:AppName     = 'Screen Time Guardian'
$script:AppNameHeb  = 'שומר זמן מסך'

# =============================================================================
# עיצוב
# =============================================================================
function Write-Header {
    param([string]$Text)
    Write-Host ''
    Write-Host ('=' * 50) -ForegroundColor DarkCyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ('=' * 50) -ForegroundColor DarkCyan
    Write-Host ''
}

function Write-Step {
    param([string]$Text)
    Write-Host "  ✓ " -ForegroundColor Green -NoNewline
    Write-Host $Text
}

function Write-Warn {
    param([string]$Text)
    Write-Host "  ⚠ " -ForegroundColor Yellow -NoNewline
    Write-Host $Text
}

function Write-Err {
    param([string]$Text)
    Write-Host "  ✗ " -ForegroundColor Red -NoNewline
    Write-Host $Text
}

function Green  { Write-Host $args[0] -ForegroundColor Green  }
function Yellow { Write-Host $args[0] -ForegroundColor Yellow }
function Red    { Write-Host $args[0] -ForegroundColor Red    }
function Cyan   { Write-Host $args[0] -ForegroundColor Cyan   }

# =============================================================================
# עזרים
# =============================================================================
function Test-Admin {
    $current = [Security.Principal.WindowsPrincipal]::new(
        [Security.Principal.WindowsIdentity]::GetCurrent())
    return $current.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Admin {
    if (-not (Test-Admin)) {
        Red ''
        Red 'נדרשות הרשאות מנהל.'
        Red 'לחץ לחיצה ימנית על הקובץ ← הפעל כמנהל (Run as administrator).'
        Red ''
        Read-Host 'לחץ Enter לסיום'
        exit 1
    }
}

function Test-FolderContents {
    param([string]$Name, [string]$RequiredFile)
    $path = Join-Path $SourceFolder $Name
    if (-not (Test-Path $path -PathType Container)) {
        return $false
    }
    if ($RequiredFile -and -not (Test-Path (Join-Path $path $RequiredFile) -PathType Leaf)) {
        return $false
    }
    return $true
}

function Safe-RemoveItem {
    param([string]$Path)
    if (Test-Path $Path) {
        Remove-Item -Path $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Set-DataDirectoryAcl {
    param([Parameter(Mandatory)][string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    $acl = Get-Acl -Path $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) {
        $acl.RemoveAccessRuleSpecific($rule) | Out-Null
    }

    $inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    $none = [System.Security.AccessControl.PropagationFlags]::None
    $allow = [System.Security.AccessControl.AccessControlType]::Allow
    foreach ($identity in @('NT AUTHORITY\SYSTEM', 'BUILTIN\Administrators')) {
        $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity, 'FullControl', $inherit, $none, $allow))
    }
    $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        'BUILTIN\Users', 'ReadAndExecute', $inherit, $none, $allow))
    Set-Acl -Path $Path -AclObject $acl
}

# =============================================================================
# קיצורי דרך
# =============================================================================
function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$Description,
        [string]$IconPath = ''
    )

    $workDir = Split-Path $TargetPath -Parent
    $iconArg = ''
    if ($IconPath -and (Test-Path $IconPath)) {
        $iconArg = "`n`$sc.IconLocation = '$IconPath'"
    }

    $script = @"
`$sc = (New-Object -ComObject WScript.Shell).CreateShortcut('$ShortcutPath')
`$sc.TargetPath = '$TargetPath'
`$sc.WorkingDirectory = '$workDir'
`$sc.Description = '$Description'$iconArg
`$sc.Save()
"@

    $psFile = Join-Path $env:TEMP "stg_shortcut_$([System.IO.Path]::GetRandomFileName()).ps1"
    # UTF-8 with BOM: Windows PowerShell 5.1 parses a BOM-less file as ANSI and
    # mangles the Hebrew shortcut name into '????'.
    [System.IO.File]::WriteAllText($psFile, $script, [System.Text.UTF8Encoding]::new($true))
    try {
        $result = Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$psFile`"" -Wait -NoNewWindow -PassThru
    }
    finally {
        Remove-Item $psFile -ErrorAction SilentlyContinue
    }
}

function Install-Shortcuts {
    param([string]$TargetExe, [switch]$DesktopShortcut)

    $iconPath = Join-Path (Split-Path $TargetExe -Parent) 'icon.ico'
    if (-not (Test-Path $iconPath)) { $iconPath = '' }

    # Start Menu folder
    $startMenuDir = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $AppName
    New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null

    $startMenuLnk = Join-Path $startMenuDir "$AppNameHeb.lnk"
    New-Shortcut -ShortcutPath $startMenuLnk -TargetPath $TargetExe `
        -Description $AppNameHeb -IconPath $iconPath
    Write-Step "קיצור דרך בתפריט ההתחל: $startMenuDir"

    # Desktop shortcut (optional)
    if ($DesktopShortcut) {
        $desktopDir = [Environment]::GetFolderPath('CommonDesktopDirectory')
        $desktopLnk = Join-Path $desktopDir "$AppNameHeb.lnk"
        New-Shortcut -ShortcutPath $desktopLnk -TargetPath $TargetExe `
            -Description $AppNameHeb -IconPath $iconPath
        Write-Step "קיצור דרך על שולחן העבודה"
    }
}

function Remove-Shortcuts {
    # Start Menu
    $startMenuDir = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $AppName
    Safe-RemoveItem $startMenuDir

    # Desktop
    $desktopDir = [Environment]::GetFolderPath('CommonDesktopDirectory')
    $desktopLnk = Join-Path $desktopDir "$AppNameHeb.lnk"
    if (Test-Path $desktopLnk) { Remove-Item $desktopLnk -Force -ErrorAction SilentlyContinue }

    # Also check per-user desktop
    $userDesktop = [Environment]::GetFolderPath('Desktop')
    $userDesktopLnk = Join-Path $userDesktop "$AppNameHeb.lnk"
    if (Test-Path $userDesktopLnk) { Remove-Item $userDesktopLnk -Force -ErrorAction SilentlyContinue }
}

# =============================================================================
# EMERGENCY STOP
# =============================================================================
function Invoke-EmergencyStop {
    Assert-Admin

    Write-Header "עצירת חירום — $AppNameHeb"

    Write-Host '  יוצר קובץ SAFEMODE...'
    New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
    Set-Content -Path (Join-Path $DataDir 'SAFEMODE') -Value (Get-Date -Format 'u') -Force
    Write-Step 'מצב בטוח הופעל. האכיפה תיעצר תוך 15 שניות.'

    Write-Host '  מסיר חוקי חומת אש...'
    Get-NetFirewallRule -Name 'STG-App-*'     -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Get-NetFirewallRule -Name 'STG-Website-*' -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Write-Step 'חוקי חומת האש הוסרו.'

    Write-Host '  מסיר חסימות הפעלה (IFEO)...'
    $ifeoRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'
    if (Test-Path $ifeoRoot) {
        Get-ChildItem $ifeoRoot | ForEach-Object {
            $props = Get-ItemProperty -Path $_.PSPath -Name 'STGOwned' -ErrorAction SilentlyContinue
            if ($null -ne $props -and $props.STGOwned -eq 'ScreenTimeGuardian') {
                Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Step 'חסימות ההפעלה הוסרו.'

    Write-Host ''
    Green '  === האכיפה נעצרה ==='
    Green "  לביטול מצב הבטוח: מחק את $DataDir\SAFEMODE"
    Write-Host ''
    Read-Host '  לחץ Enter לסיום'
}

if ($EmergencyStop) {
    Invoke-EmergencyStop
    exit 0
}

# =============================================================================
# UNINSTALL
# =============================================================================
function Invoke-Uninstall {
    Assert-Admin

    Write-Header "הסרת $AppNameHeb"

    # Confirmation
    Write-Host "  האם אתה בטוח שברצונך להסיר את $AppNameHeb?" -ForegroundColor Yellow
    Write-Host "  כל ההגדרות יימחקו." -ForegroundColor Yellow
    Write-Host ''
    $confirm = Read-Host '  הקלד "yes" כדי לאשר'
    if ($confirm -ne 'yes') {
        Yellow '  בוטל.'
        Read-Host '  לחץ Enter לסיום'
        exit 0
    }

    Write-Host ''

    # 1. Stop and delete service
    Write-Host '  1/7 עוצר את השירות...'
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne 'Stopped') {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 3
        }
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
        Write-Step 'השירות הוסר.'
    }
    else {
        Write-Step 'השירות לא נמצא (כבר הוסר).'
    }

    # 2. Remove firewall rules
    Write-Host '  2/7 מסיר חוקי חומת אש...'
    Get-NetFirewallRule -Name 'STG-App-*' -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Get-NetFirewallRule -Name 'STG-Website-*' -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Write-Step 'חוקי חומת האש הוסרו.'

    # 3. Remove IFEO entries
    Write-Host '  3/7 מסיר חסימות הפעלה...'
    $ifeoRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'
    if (Test-Path $ifeoRoot) {
        Get-ChildItem $ifeoRoot | ForEach-Object {
            $props = Get-ItemProperty -Path $_.PSPath -Name 'STGOwned' -ErrorAction SilentlyContinue
            if ($null -ne $props -and $props.STGOwned -eq 'ScreenTimeGuardian') {
                Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Step 'חסימות ההפעלה הוסרו.'

    # 4. Remove agent autorun
    Write-Host '  4/7 מסיר סוכן מהפעלה אוטומטית...'
    Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' `
        -Name 'ScreenTimeGuardianAgent' -ErrorAction SilentlyContinue
    Write-Step 'הסוכן הוסר מהפעלה אוטומטית.'

    # 5. Remove NativeHost registration
    Write-Host '  5/7 מסיר רישום Native Host...'
    Remove-Item -Path 'HKLM:\Software\Google\Chrome\NativeMessagingHosts\com.screentimeguardian.host' `
        -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path 'HKLM:\Software\Microsoft\Edge\NativeMessagingHosts\com.screentimeguardian.host' `
        -Recurse -Force -ErrorAction SilentlyContinue
    Safe-RemoveItem (Join-Path $DataDir 'com.screentimeguardian.host.json')
    Write-Step 'רישום Native Host הוסר.'

    # 6. Remove shortcuts
    Write-Host '  6/7 מסיר קיצורי דרך...'
    Remove-Shortcuts
    Write-Step 'קיצורי הדרך הוסרו.'

    # 7. Remove installation directory and data
    Write-Host '  7/7 מוחק קבצים...'
    Safe-RemoveItem $InstallRoot
    Safe-RemoveItem $DataDir
    Write-Step 'קבצי ההתקנה וההגדרות נמחקו.'

    Write-Host ''
    Green ('=' * 50)
    Green "  $AppNameHeb הוסר בהצלחה!"
    Green ('=' * 50)
    Write-Host ''
    Read-Host '  לחץ Enter לסיום'
}

if ($Uninstall) {
    Invoke-Uninstall
    exit 0
}

# =============================================================================
# INSTALL
# =============================================================================
Assert-Admin

# ---- Normalize package layout ------------------------------------------------
# החבילה מאורגנת בשמות מלאים (ScreenTimeGuardian.Service\ וכו'), אבל חלק מהחלקים
# של המתקין משתמשים בשמות קצרים (Service\ וכו'). יוצר תיקיות קצרות אם חסרות.
$componentFolders = @(
    @{ Short = 'Service';       Full = 'ScreenTimeGuardian.Service' },
    @{ Short = 'ControlPanel';  Full = 'ScreenTimeGuardian.ControlPanel' },
    @{ Short = 'NativeHost';    Full = 'ScreenTimeGuardian.NativeHost' },
    @{ Short = 'Agent';         Full = 'ScreenTimeGuardian.Agent' },
    @{ Short = 'LaunchBlocker'; Full = 'ScreenTimeGuardian.LaunchBlocker' },
    @{ Short = 'Updater';       Full = 'ScreenTimeGuardian.Updater' }
)

foreach ($pair in $componentFolders) {
    $shortPath = Join-Path $SourceFolder $pair.Short
    $fullPath  = Join-Path $SourceFolder $pair.Full
    if (-not (Test-Path $shortPath -PathType Container) -and (Test-Path $fullPath -PathType Container)) {
        New-Item -ItemType Directory -Force -Path $shortPath | Out-Null
        Copy-Item -Path (Join-Path $fullPath '*') -Destination $shortPath -Recurse -Force
    }
}

# הבנייה מייצרת קובץ .exe יחיד (PublishSingleFile) — יוצר עותק .dll
# כדי שבדיקות תקינות החבילה והסקריפטים הישנים יעבדו.
$serviceExePath = Join-Path $SourceFolder 'Service\ScreenTimeGuardian.Service.exe'
$serviceDllPath = Join-Path $SourceFolder 'Service\ScreenTimeGuardian.Service.dll'
if (-not (Test-Path $serviceDllPath -PathType Leaf) -and (Test-Path $serviceExePath -PathType Leaf)) {
    Copy-Item -Path $serviceExePath -Destination $serviceDllPath -Force
}

Write-Header "$AppNameHeb — התקנה v$Version"

# ---- 1. Validate package ---------------------------------------------------
Write-Host '  בודק את החבילה...'
$allOk = $true

$required = @(
    @{Name='Service';      File='ScreenTimeGuardian.Service.dll'},
    @{Name='ControlPanel'; File='ScreenTimeGuardian.ControlPanel.exe'},
    @{Name='NativeHost';   File='ScreenTimeGuardian.NativeHost.exe'},
    @{Name='Agent';        File='ScreenTimeGuardian.Agent.exe'},
    @{Name='Policies';     File='Install-Service.ps1'},
    @{Name='Policies';     File='Register-NativeHost.ps1'},
    @{Name='Extension';    File='manifest.json'}
)

foreach ($entry in $required) {
    $full = Join-Path $SourceFolder ($entry.Name + '\' + $entry.File)
    if (-not (Test-Path $full -PathType Leaf)) {
        Write-Err "חסר: $($entry.Name)\$($entry.File)"
        $allOk = $false
    }
}

if (-not $allOk) {
    Write-Host ''
    Red '  החבילה אינה שלמה.'
    Red '  ודא שחילצת את קובץ ה-ZIP במלואו.'
    Read-Host '  לחץ Enter לסיום'
    exit 1
}
Write-Step 'החבילה תקינה.'

# ---- 2. Secure data directory ----------------------------------------------
Set-DataDirectoryAcl -Path $DataDir
Set-DataDirectoryAcl -Path (Join-Path $DataDir 'runtime')
Write-Step 'תיקיית נתונים מאובטחת.'

# ---- 4. Stop existing service ----------------------------------------------
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne 'Stopped') {
    Write-Host '  עוצר שירות קיים...'
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
}

# ---- 5. Copy files ----------------------------------------------------------
$sourceFull = [System.IO.Path]::GetFullPath($SourceFolder).TrimEnd('\')
$targetFull = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')

if ($sourceFull -eq $targetFull) {
    Write-Step 'הקבצים כבר במקום.'
}
else {
    Write-Host "  מעתיק אל $InstallRoot..."
    if (Test-Path $InstallRoot) {
        # עוצר תהליכים שעשויים לנעול קבצים (סוכן/לוח בקרה)
        Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessName -match '^ScreenTimeGuardian\.(Agent|ControlPanel|NativeHost|Updater)$'
        } | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Remove-Item -Path $InstallRoot -Recurse -Force -ErrorAction Stop
    }
    Copy-Item -Path $SourceFolder -Destination $InstallRoot -Recurse -Force
    Write-Step 'הקבצים הועתקו.'
}

$policiesDir = Join-Path $InstallRoot 'Policies'

# ---- 6. Install service -----------------------------------------------------
Write-Host '  מתקין את השירות...'
$serviceExe = Join-Path $InstallRoot 'Service\ScreenTimeGuardian.Service.exe'
if (-not (Test-Path $serviceExe -PathType Leaf)) {
    Red "  קובץ השירות לא נמצא: $serviceExe"
    Read-Host '  לחץ Enter לסיום'
    exit 1
}

& (Join-Path $policiesDir 'Install-Service.ps1') `
    -ServiceExecutable $serviceExe `
    -StartAfterInstall | Out-Null
Write-Step 'השירות הותקן והופעל.'

# ---- 7. Register NativeHost (auto — no questions) --------------------------
Write-Host '  רושם את Native Messaging Host...'
$manifestPath = Join-Path $SourceFolder 'Extension\manifest.json'
$registerScript = Join-Path $policiesDir 'Register-NativeHost.ps1'

# מחשב את מזהה התוסף (32 תווים a-p) מה-key שב-manifest.json —
# אותו אלגוריתם שבו Chrome גוזר את ה-ID מהמפתח הציבורי.
$extensionId = ''
if (Test-Path $manifestPath -PathType Leaf) {
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.key) {
            $keyBytes = [Convert]::FromBase64String($manifest.key)
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                $hash = $sha.ComputeHash($keyBytes)
            }
            finally {
                $sha.Dispose()
            }
            $extensionId = -join ($hash[0..15] | ForEach-Object {
                [char]([int][char]'a' + ($_ -shr 4))
                [char]([int][char]'a' + ($_ -band 0x0f))
            })
        }
    }
    catch {
        $extensionId = ''
    }
}

if (-not $extensionId) {
    Write-Warn 'לא ניתן היה לחשב את מזהה התוסף (key חסר ב-manifest).'
    Write-Warn 'הרץ Register-NativeHost.ps1 ידנית לאחר התקנת התוסף.'
}
else {
    try {
        & $registerScript -ExtensionId $extensionId -EdgeExtensionId $extensionId 2>&1 | Out-Null
        Write-Step "Native Messaging Host נרשם (מזהה תוסף $extensionId)."
    }
    catch {
        Write-Warn "רישום NativeHost נכשל: $($_.Exception.Message)"
        Write-Warn 'הרץ Register-NativeHost.ps1 ידנית לאחר ההתקנה.'
    }
}

# ---- 8. Agent (auto — always install) ---------------------------------------
Write-Host '  מתקין את סוכן ההתראות...'
$agentExe = Join-Path $InstallRoot 'Agent\ScreenTimeGuardian.Agent.exe'
if (Test-Path $agentExe -PathType Leaf) {
    New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' `
        -Name 'ScreenTimeGuardianAgent' `
        -Value "`"$agentExe`"" `
        -PropertyType String `
        -Force | Out-Null
    Write-Step 'הסוכן הותקן בהפעלה האוטומטית.'
}
else {
    Write-Warn "הסוכן לא נמצא: $agentExe"
}

# ---- 9. Create shortcuts (automatic — desktop + Start menu) -----------------
Write-Host '  יוצר קיצורי דרך (שולחן עבודה + תפריט התחל)...'
$controlPanelExe = Join-Path $InstallRoot 'ControlPanel\ScreenTimeGuardian.ControlPanel.exe'
if (Test-Path $controlPanelExe -PathType Leaf) {
    Install-Shortcuts -TargetExe $controlPanelExe -DesktopShortcut
}
else {
    Write-Warn 'ControlPanel.exe לא נמצא — קיצורי דרך לא נוצרו.'
}

# ---- 10. Register in Add/Remove Programs ------------------------------------
Write-Host '  רושם את התוכנה בהוספה/הסרה...'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenTimeGuardian'
try {
    New-Item -Path $uninstallKey -Force | Out-Null
    Set-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value $AppNameHeb
    Set-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value $Version
    Set-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value 'Screen Time Guardian'
    Set-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $InstallRoot
    Set-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$($InstallRoot)\Uninstall.ps1`""
    Set-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value "$InstallRoot\ControlPanel\icon.ico"
    Set-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -Type DWord
    Set-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -Type DWord
    Write-Step 'נרשם בהוספה/הסרה של Windows.'
}
catch {
    Write-Warn 'רישום בהוספה/הסרה נכשל (לא קריטי).'
}

# ---- 11. Create uninstaller script ------------------------------------------
$uninstallScript = @"
# Auto-generated uninstaller for $AppNameHeb
`$scriptDir = Split-Path -Parent `$MyInvocation.MyCommand.Path
& "`$scriptDir\Policies\Install-Service.ps1" -ServiceExecutable "$serviceExe" -Uninstall -StartAfterInstall
"@
$uninstallPath = Join-Path $InstallRoot 'Uninstall.ps1'
[System.IO.File]::WriteAllText($uninstallPath, $uninstallScript, [System.Text.Encoding]::UTF8)

# ---- Summary ---------------------------------------------------------------
Write-Host ''
Green ('=' * 50)
Green "  $AppNameHeb הותקן בהצלחה!"
Green ('=' * 50)
Write-Host ''
Cyan '  מה הלאה:'
Cyan '  1. פתח את לוח הבקרה מהתפריט ההתחל'
Cyan '  2. הגדר סיסמת מנהל בהפעלה הראשונה'
Cyan '  3. התקן את תוסף הדפדפן (Chrome/Edge)'
Cyan '  4. צור כלל חסימה ראשון'
Cyan '  5. אתחל את המחשב וודא שהכל עובד'
Write-Host ''
Green "  נתיב התקנה: $InstallRoot"
Green "  נתיב הגדרות: $DataDir"
Write-Host ''

# Launch option
$launchChoice = Read-Host '  לפתוח את לוח הבקרה עכשיו? (yes/no, ברירת מחדל yes)'
if ($launchChoice -ne 'no') {
    if (Test-Path $controlPanelExe) {
        Start-Process $controlPanelExe
        Write-Host ''
        Green '  לוח הבקרה נפתח.'
    }
}

Write-Host ''
Read-Host '  לחץ Enter לסיום'
