<#
    Panic switch. Stops ALL enforcement immediately without uninstalling anything.
    Safe to run at any time. Does not require the application password.
#>
Set-StrictMode -Version Latest

$dataDirectory = 'C:\ProgramData\ScreenTimeGuardian'
$flag = Join-Path $dataDirectory 'SAFEMODE'

if (-not (Test-Path $dataDirectory)) {
    Write-Host 'התוכנה אינה מותקנת.' -ForegroundColor Yellow
    return
}

Set-Content -Path $flag -Value (Get-Date -Format 'u') -Force
Write-Host 'מצב בטוח הופעל. האכיפה תיעצר תוך 15 שניות.' -ForegroundColor Green
Write-Host "כדי לבטל: מחק את הקובץ $flag" -ForegroundColor Yellow

Write-Host ''
Write-Host 'מסיר גם חוקי חומת אש קיימים (דורש הרשאת מנהל)...'
try {
    Get-NetFirewallRule -Name 'STG-App-*'     -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Get-NetFirewallRule -Name 'STG-Website-*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
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
    Write-Host 'החוקים וחסימות ההפעלה הוסרו.' -ForegroundColor Green
} catch {
    Write-Host 'לא הוסרו חוקים (ייתכן שאין הרשאת מנהל). מצב בטוח עדיין פעיל.' -ForegroundColor Yellow
}
