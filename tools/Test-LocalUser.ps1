<#
    Screen Time Guardian – בדיקת -LocalUser בחומת האש
    מפיק דוח טקסט על שולחן העבודה.

    לחיצה כפולה = דוח.

    חשוב: בקש מאדם אחר (אח, לא המשתמש המוגבל) להשתתף בבדיקה.
#>
param([string]$ReportPath = "$env:USERPROFILE\Desktop\STG-Test-Report.txt")

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$lines = [System.Collections.Generic.List[string]]::new()

function Log { $lines.Add($args[0]) }
function Green { Write-Host $args[0] -ForegroundColor Green; Log $args[0] }
function Yellow { Write-Host $args[0] -ForegroundColor Yellow; Log $args[0] }
function Red { Write-Host $args[0] -ForegroundColor Red; Log $args[0] }
function Cyan { Write-Host $args[0] -ForegroundColor Cyan; Log $args[0] }

Green "=== Screen Time Guardian - `u05D1`$u05D3`$u05D9`$u05E7`$u05EA LocalUser ==="
Green "זמן: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Green ''

# 1. Check admin
$current = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $current.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Red 'אין הרשאות מנהל. לחץ ימני על Test-LocalUser.cmd ← הפעל כמנהל.'
    Read-Host
    exit 1
}
Green 'הרשאות מנהל: תקין'

# 2. Pick a user
Green ''
$userName = Read-Host 'שם משתמש Windows לבדיקה (למשל: yossi)'
if ([string]::IsNullOrWhiteSpace($userName)) {
    Red 'לא הוזן שם משתמש. יוצא.'
    Read-Host
    exit 1
}

# 3. Resolve SID
try {
    $ntAccount = [Security.Principal.NTAccount]::new($userName)
    $sid = $ntAccount.Translate([Security.Principal.SecurityIdentifier]).Value
    Green "SID: $sid"
}
catch {
    Red "לא ניתן למצוא את המשתמש '$userName'. בדוק את השם."
    Read-Host
    exit 1
}

# 4. Clean up any previous test rule
Get-NetFirewallRule -Name 'STG-TEST' -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction SilentlyContinue

# 5. Create test rule
Green ''
Green 'יוצר חוק בדיקה...'
try {
    New-NetFirewallRule -Name 'STG-TEST' -DisplayName 'STG Test' `
        -Direction Outbound -Action Block -Enabled True -Profile Any `
        -Program 'C:\Windows\System32\notepad.exe' `
        -LocalUser "D:(A;;CC;;;$sid)"
    Green 'החוק נוצר.'
}
catch {
    Red "יצירת החוק נכשלה. מנסה פורמט SDDL חלופי: (A;;CC;;;$sid)"
    try {
        New-NetFirewallRule -Name 'STG-TEST' -DisplayName 'STG Test' `
            -Direction Outbound -Action Block -Enabled True -Profile Any `
            -Program 'C:\Windows\System32\notepad.exe' `
            -LocalUser "(A;;CC;;;$sid)"
        Green 'החוק נוצר (פורמט חלופי).'
    }
    catch {
        Red "יצירת החוק נכשלה גם בפורמט חלופי: $_"
        Remove-NetFirewallRule -Name 'STG-TEST' -ErrorAction SilentlyContinue
        Read-Host
        exit 1
    }
}

# 6. Show the rule
$rule = Get-NetFirewallRule -Name 'STG-TEST'
$filter = $rule | Get-NetFirewallSecurityFilter
Green ''
Green '--- פרטי החוק ---'
Green "  שם: $($rule.Name)"
Green "  כיוון: $($rule.Direction)"
Green "  פעולה: $($rule.Action)"
Green "  תוכנית: $($rule.Program)"
Green "  LocalUser: $($filter.LocalUser)"
Green '-------------------'

# 7. Instructions
Green ''
Green '========================================'
Yellow '  עכשיו בצע את שתי הבדיקות האלה:'
Green ''
Cyan '  בדיקה א:'
Cyan "    1. התחבר ל־Windows כמשתמש '$userName'"
Cyan '    2. פתח את Notepad'
Cyan '    3. נסה לגלוש (Help ← View Help)'
Cyan '    4. ציפייה: אין אינטרנט'
Green ''
Cyan '  בדיקה ב:'
Cyan '    1. התחבר כמשתמש Windows אחר'
Cyan '    2. פתח את Notepad'
Cyan '    3. נסה לגלוש (Help ← View Help)'
Cyan '    4. ציפייה: יש אינטרנט'
Green ''
Yellow '  הקש ENTER אחרי שביצעת את שתי הבדיקות'
Yellow '  כדי לנקות את חוק הבדיקה.'
Green '========================================'
Read-Host | Out-Null

# 8. Ask for results
$resultA = Read-Host 'בדיקה א (המשתמש '$userName') — לא היה אינטרנט? (yes/no)'
$resultB = Read-Host 'בדיקה ב (משתמש אחר) — היה אינטרנט? (yes/no)'

# 9. Cleanup
Green 'מנקה...'
Remove-NetFirewallRule -Name 'STG-TEST' -ErrorAction SilentlyContinue
Green 'החוק הוסר.'

# 10. Report
Green ''
Green '========================================'
Green '  תוצאות הבדיקה'
Green '========================================'

$passedA = ($resultA -eq 'yes')
$passedB = ($resultB -eq 'yes')

if ($passedA -and $passedB) {
    Green ''
    Green '✓ הבדיקה עברה בהצלחה!'
    Green '  מודל "רק אני" עובד: החוק חל רק על המשתמש המיועד.'
    Green '  Screen Time Guardian פועל כמתוכנן.'
    Log "PASS: Both tests passed"
}
elseif ($passedA -and -not $passedB) {
    Red ''
    Red '✗ הבדיקה נכשלה!'
    Red '  החוק חל על כל המשתמשים, לא רק על $userName.'
    Red '  "חסימה לפי משתמש" אינה עובדת בגרסת Windows זו.'
    Red '  יש להגדיר EnforceForAdministrators = false ולהתייחס לחסימה כגלובלית.'
    Log "FAIL: Rule applies to all users (B failed)"
}
elseif (-not $passedA) {
    Yellow ''
    Yellow '? בדיקה א נכשלה — למשתמש המיועד היה אינטרנט.'
    Yellow '  ייתכן שהחוק לא נאכף. בדוק את הגדרות חומת האש.'
    Log "INCONCLUSIVE: User A had internet"
}

# 11. Write report
$report = $lines -join "`r`n"
try {
    [System.IO.File]::WriteAllText($ReportPath, $report, [System.Text.UTF8Encoding]::new($true))
    Green ''
    Green "הדוח נשמר: $ReportPath"
}
catch {
    Yellow "לא ניתן היה לשמור את הדוח: $_"
    Yellow 'הנה הדוח עצמו:'
    Write-Host $report
}

Read-Host 'לחץ Enter לסיום'