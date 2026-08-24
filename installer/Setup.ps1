<#
    Screen Time Guardian – מתקין אחיד v0.4.8+

    לחיצה כפולה על Install.cmd = התקנה מלאה.
    לחיצה כפולה על Uninstall.cmd = הסרה מלאה.
    לחיצה כפולה על Emergency-Stop.cmd = עצירת כל אכיפה.

    סקריפט זה הוא המנוע שמאחורי שלושת קובצי ה־.cmd.
    אל תריץ אותו ישירות אלא דרך קובץ ה־.cmd המתאים.
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
$script:Version     = '0.4.8'

# =============================================================================
# צבעים
# =============================================================================
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
        Red 'אין הרשאות מנהל.'
        Red 'לחיצה ימנית על הקובץ ← הפעל כמנהל (Run as administrator).'
        Read-Host
        exit 1
    }
}

function Test-FolderContents {
    param([string]$Name, [string]$RequiredFile)
    $path = Join-Path $SourceFolder $Name
    if (-not (Test-Path $path -PathType Container)) {
        Red "חסרה תיקייה: $Name"
        Red 'החבילה שחולצה אינה שלמה. חלץ שוב את ה־ZIP.'
        Read-Host
        exit 1
    }
    if ($RequiredFile -and -not (Test-Path (Join-Path $path $RequiredFile) -PathType Leaf)) {
        Yellow "אזהרה: חסר קובץ $RequiredFile בתיקייה $Name"
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

# =============================================================================
# EMERGENCY STOP
# =============================================================================
function Invoke-EmergencyStop {
    Assert-Admin
    Green '=== עצירת חירום ==='
    Green 'יוצר קובץ SAFEMODE...'
    New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
    Set-Content -Path (Join-Path $DataDir 'SAFEMODE') -Value (Get-Date -Format 'u') -Force
    Green 'מצב בטוח הופעל. האכיפה תיעצר תוך 15 שניות.'

    Green 'מסיר חוקי חומת אש...'
    Get-NetFirewallRule -Name 'STG-App-*'     -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Get-NetFirewallRule -Name 'STG-Website-*' -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue

    Green 'מסיר חסימות הפעלה (IFEO)...'
    $ifeoRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'
    if (Test-Path $ifeoRoot) {
        Get-ChildItem $ifeoRoot | ForEach-Object {
            $properties = Get-ItemProperty -Path $_.PSPath -Name 'STGOwned' -ErrorAction SilentlyContinue
            if ($null -ne $properties -and $properties.PSObject.Properties.Name -contains 'STGOwned' -and
                $properties.STGOwned -eq 'ScreenTimeGuardian') {
                Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
                Yellow "  הוסר: $($_.PSChildName)"
            }
        }
    }

    Green ''
    Green '=== האכיפה נעצרה ==='
    Green 'התוכנה עדיין מותקנת. כדי להסיר לגמרי, הרץ Uninstall.cmd.'
    Green "לביטול מצב הבטוח: מחק את $DataDir\SAFEMODE"
    Read-Host
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

    Green '=== הסרת Screen Time Guardian ==='
    Green ''

    # Stop and delete service
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne 'Stopped') {
            Stop-Service -Name $ServiceName -Force
            Start-Sleep -Seconds 3
        }
        sc.exe delete $ServiceName | Out-Null
        Green 'השירות הוסר.'
    }
    else {
        Yellow 'השירות לא נמצא (ייתכן שכבר הוסר).'
    }

    # Firewall rules
    Green 'מסיר חוקי חומת אש...'
    Get-NetFirewallRule -Name 'STG-App-*'     -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Get-NetFirewallRule -Name 'STG-Website-*' -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Green 'חוקי חומת האש הוסרו.'

    # IFEO entries
    Green 'מסיר חסימות הפעלה של דפדפנים...'
    $ifeoRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'
    if (Test-Path $ifeoRoot) {
        Get-ChildItem $ifeoRoot | ForEach-Object {
            $properties = Get-ItemProperty -Path $_.PSPath -Name 'STGOwned' -ErrorAction SilentlyContinue
            if ($null -ne $properties -and $properties.PSObject.Properties.Name -contains 'STGOwned' -and
                $properties.STGOwned -eq 'ScreenTimeGuardian') {
                Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Green 'חסימות ההפעלה הוסרו.'

    # Autorun agent
    Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' `
        -Name 'ScreenTimeGuardianAgent' -ErrorAction SilentlyContinue

    # NativeHost registration
    Remove-Item -Path 'HKLM:\Software\Google\Chrome\NativeMessagingHosts\com.screentimeguardian.host' `
        -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path 'HKLM:\Software\Microsoft\Edge\NativeMessagingHosts\com.screentimeguardian.host' `
        -Recurse -Force -ErrorAction SilentlyContinue
    Safe-RemoveItem (Join-Path $DataDir 'com.screentimeguardian.host.json')

    # Configuration data
    if (Test-Path $DataDir) {
        $answer = Read-Host 'למחוק גם את ההגדרות הקיימות? (yes/no)'
        if ($answer -eq 'yes') {
            Safe-RemoveItem $DataDir
            Green 'ההגדרות נמחקו.'
        }
        else {
            Yellow 'ההגדרות נשמרו.'
        }
    }

    Green ''
    Green '=== ההסרה הושלמה ==='
    Green "תיקיית ההתקנה $InstallRoot אינה נמחקת אוטומטית (עלולה להכיל קבצים אישיים)."
    Yellow "אפשר למחוק אותה ידנית: Remove-Item -Recurse -Force '$InstallRoot'"
    Read-Host
}

if ($Uninstall) {
    Invoke-Uninstall
    exit 0
}

# =============================================================================
# INSTALL
# =============================================================================
Assert-Admin

Green '========================================'
Green "  Screen Time Guardian - התקנה v$Version"
Green '========================================'
Green ''

# ---- 1. Validate package ---------------------------------------------------
Green 'בודק את החבילה...'

$required = @(
    @{Name='Service';           File='ScreenTimeGuardian.Service.dll'},
    @{Name='ControlPanel';      File='ScreenTimeGuardian.ControlPanel.exe'},
    @{Name='NativeHost';        File='ScreenTimeGuardian.NativeHost.exe'},
    @{Name='Agent';             File='ScreenTimeGuardian.Agent.exe'},
    @{Name='Policies';          File='Install-Service.ps1'},
    @{Name='Policies';          File='Register-NativeHost.ps1'},
    @{Name='Extension';         File='manifest.json'},
    @{Name='Policies';          File='Emergency-Disable.ps1'}
)

$allOk = $true
foreach ($entry in $required) {
    $full = Join-Path $SourceFolder ($entry.Name + '\' + $entry.File)
    if (-not (Test-Path $full -PathType Leaf)) {
        Red "  חסר: $($entry.Name)\$($entry.File)"
        $allOk = $false
    }
}

if (-not $allOk) {
    Red ''
    Red 'החבילה שחולצה אינה שלמה.'
    Red 'ודא שחילצת את קובץ ה־ZIP במלואו לתיקייה חדשה.'
    Read-Host
    exit 1
}

$extensionData = [System.IO.File]::ReadAllText(
    (Join-Path $SourceFolder 'Extension\manifest.json'))
if ($extensionData -notmatch '"key"') {
    Yellow ''
    Yellow 'אזהרה: שדה "key" חסר ב־manifest.json.'
    Yellow 'בלי מפתח קבוע, מזהה התוסף ישתנה בכל טעינה.'
    Yellow 'פעל לפי docs/ExtensionSetup.md (נמצא בתיקיית Docs).'
    Yellow ''
}
Green 'החבילה תקינה.'

# ---- 2. Stop running service before copy ------------------------------------
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne 'Stopped') {
    Green 'עוצר שירות קיים...'
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 3
}

# ---- 3. Copy files ----------------------------------------------------------
$sourceFull = [System.IO.Path]::GetFullPath($SourceFolder).TrimEnd('\')
$targetFull = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
if ($sourceFull -eq $targetFull) {
    Green 'הקבצים כבר נמצאים בתיקיית היעד. מדלג על העתקה.'
}
else {
    Green "מעתיק אל $InstallRoot ..."
    if (Test-Path $InstallRoot) {
        Remove-Item -Path $InstallRoot -Recurse -Force
    }
    Copy-Item -Path $SourceFolder -Destination $InstallRoot -Recurse -Force
    Green 'הקבצים הועתקו.'
}

$policiesDir = Join-Path $InstallRoot 'Policies'

# ---- 4. Install service -----------------------------------------------------
Green ''
Green 'מתקין את שירות Windows...'
$serviceExe = Join-Path $InstallRoot 'Service\ScreenTimeGuardian.Service.exe'
if (-not (Test-Path $serviceExe -PathType Leaf)) {
    Red "קובץ השירות לא נמצא: $serviceExe"
    Read-Host
    exit 1
}

& (Join-Path $policiesDir 'Install-Service.ps1') `
    -ServiceExecutable $serviceExe `
    -StartAfterInstall

# ---- 5. Register NativeHost ------------------------------------------------
Green ''
Green 'רושם את Native Messaging Host...'
$registerScript = Join-Path $policiesDir 'Register-NativeHost.ps1'

if ($extensionData -match 'EXTENSION_ID_PLACEHOLDER') {
    Yellow ''
    Yellow '========================================'
    Yellow '  שימו לב!'
    Yellow '========================================'
    Yellow 'NativeHost.manifest.json עדיין מכיל EXTENSION_ID_PLACEHOLDER.'
    Yellow 'רישום Native Host ידרוש מזהי Chrome ו־Edge אמיתיים.'
    Yellow ''
    Yellow 'הרץ ידנית לאחר התקנת התוסף:'
    Yellow "  cd '$policiesDir'"
    Yellow "  .\Register-NativeHost.ps1 -ExtensionId '<מזהה>' -EdgeExtensionId '<מזהה>'"
    Yellow ''
    Yellow 'מדריך מלא: docs/ExtensionSetup.md (נמצא בתיקיית Docs).'

    $answer = Read-Host 'להריץ את הרישום עכשיו? (yes/no, ברירת מחדל no)'
    if ($answer -eq 'yes') {
        $chromeId  = Read-Host 'מזהה Chrome (32 תווים)'
        $edgeId    = Read-Host 'מזהה Edge   (32 תווים)'
        if ($chromeId.Length -eq 32 -and $edgeId.Length -eq 32) {
            & $registerScript -ExtensionId $chromeId -EdgeExtensionId $edgeId
        }
        else {
            Red 'מזהה לא תקין. דלג.'
        }
    }
}
else {
    # Try auto-register if a .pem exists
    $pemPath = Join-Path $InstallRoot 'Extension.pem'
    if (Test-Path $pemPath -PathType Leaf) {
        & $registerScript -KeyPath $pemPath
    }
    else {
        Yellow 'קובץ Extension.pem לא נמצא. הרץ Register-NativeHost.ps1 ידנית.'
    }
}

# ---- 6. Agent (optional) ----------------------------------------------------
Green ''
$installAgent = Read-Host 'להתקין את סוכן ההתראות? (yes/no, ברירת מחדל yes)'
if ($installAgent -ne 'no') {
    $agentExe = Join-Path $InstallRoot 'Agent\ScreenTimeGuardian.Agent.exe'
    if (Test-Path $agentExe -PathType Leaf) {
        New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' `
            -Name 'ScreenTimeGuardianAgent' `
            -Value $agentExe `
            -PropertyType String `
            -Force | Out-Null
        Green 'הסוכן נרשם בהפעלה האוטומטית.'
    }
    else {
        Yellow "הסוכן לא נמצא: $agentExe"
    }
}

# ---- 7. Summary -------------------------------------------------------------
Green ''
Green '========================================'
Green '  ההתקנה הושלמה!'
Green '========================================'
Green ''
Cyan 'השלב הבא:'
Cyan '  1. פתח את ControlPanel (נמצא בתיקיית ControlPanel)'
Cyan '  2. אבא קובע סיסמת מנהל בהפעלה הראשונה'
Cyan '  3. התקן את התוסף (מדריך בתיקיית Docs)'
Cyan '  4. צור כלל בדיקה אחד על אפליקציה לא קריטית'
Cyan '  5. אתחל את המחשב וודא שהוא עולה תקין'
Cyan ''
Green 'מתג חירום: Emergency-Stop.cmd'
Green 'הסרה מלאה: Uninstall.cmd'
Green ''
Green "נתיב ההתקנה:  $InstallRoot"
Green "נתיב ההגדרות:  $DataDir"
Green ''
Read-Host 'לחץ Enter לסיום'