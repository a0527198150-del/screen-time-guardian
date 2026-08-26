<#
    Complete removal. Run from an ELEVATED PowerShell window.
    Removes the service AND every firewall rule it created, so nothing is left
    blocking the machine after the software is gone.
#>
param(
    [string]$ServiceName = 'ScreenTimeGuardian',
    [switch]$KeepConfiguration
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$currentPrincipal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'יש להריץ סקריפט זה מחלון PowerShell עם הרשאות מנהל.'
}

Write-Host 'עוצר את כל תהליכי התוכנה...'
Get-Process -Name 'ScreenTimeGuardian*' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# מסיר כל שירות בשם ScreenTimeGuardian (גרסה נוכחית או ישנה).
$services = Get-Service -Name 'ScreenTimeGuardian*' -ErrorAction SilentlyContinue
foreach ($svc in $services) {
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $svc.Name -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    sc.exe delete $svc.Name | Out-Null
    Write-Host "השירות $($svc.Name) הוסר." -ForegroundColor Green
}

Write-Host 'מסיר חוקי חומת אש...'
Get-NetFirewallRule -Name 'STG-App-*'     -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
Get-NetFirewallRule -Name 'STG-Website-*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
Write-Host 'חוקי חומת האש הוסרו.' -ForegroundColor Green

Write-Host 'מסיר חסימות הפעלה של דפדפנים (IFEO)...'
$ifeoRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'
if (Test-Path $ifeoRoot) {
    Get-ChildItem $ifeoRoot | ForEach-Object {
        $properties = Get-ItemProperty -Path $_.PSPath -Name 'STGOwned' -ErrorAction SilentlyContinue
        if ($null -ne $properties -and $properties.PSObject.Properties.Name -contains 'STGOwned' -and
            $properties.STGOwned -eq 'ScreenTimeGuardian') {
            Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  הוסר: $($_.PSChildName)"
        }
    }
}
Write-Host 'חסימות ההפעלה הוסרו.' -ForegroundColor Green

Write-Host 'מסיר את הסוכן מההפעלה האוטומטית...'
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'ScreenTimeGuardianAgent' -ErrorAction SilentlyContinue

Write-Host 'מסיר רישום Native Host...'
Remove-Item -Path 'HKLM:\Software\Google\Chrome\NativeMessagingHosts\com.screentimeguardian.host' `
    -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKLM:\Software\Microsoft\Edge\NativeMessagingHosts\com.screentimeguardian.host' `
    -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'C:\ProgramData\ScreenTimeGuardian\com.screentimeguardian.host.json' `
    -Force -ErrorAction SilentlyContinue

Write-Host 'מסיר קיצורי דרך...'
$startMenuDir = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Screen Time Guardian'
Remove-Item -Path $startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
foreach ($desktop in @([Environment]::GetFolderPath('CommonDesktopDirectory'), [Environment]::GetFolderPath('Desktop'))) {
    Remove-Item -Path (Join-Path $desktop 'שומר זמן מסך.lnk') -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $desktop 'Screen Time Guardian.lnk') -Force -ErrorAction SilentlyContinue
}

Write-Host 'מוחק את תיקיית ההתקנה...'
$installRoot = 'C:\Program Files\ScreenTimeGuardian'
for ($attempt = 1; $attempt -le 10; $attempt++) {
    if (Test-Path $installRoot) {
        Remove-Item -Path $installRoot -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $installRoot)) { break }
        Start-Sleep -Milliseconds 800
    }
}
if (-not (Test-Path $installRoot)) {
    Write-Host 'תיקיית ההתקנה הוסרה.' -ForegroundColor Green
}
else {
    Write-Warning 'תיקיית ההתקנה לא נמחקה לחלוטין — כנראה קובץ נעול. הפעל מחדש ונסה שוב.'
}

Write-Host 'מסיר רישום מהוספה/הסרה של Windows...'
Remove-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ScreenTimeGuardian' `
    -Recurse -Force -ErrorAction SilentlyContinue

if (-not $KeepConfiguration) {
    $dataDirectory = 'C:\ProgramData\ScreenTimeGuardian'
    if (Test-Path $dataDirectory) {
        Remove-Item -Path $dataDirectory -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host 'תיקיית ההגדרות הוסרה.' -ForegroundColor Green
    }
}

Write-Host ''
Write-Host 'ההסרה הושלמה.' -ForegroundColor Green
