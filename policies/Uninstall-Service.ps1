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

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 3
    }
    sc.exe delete $ServiceName | Out-Null
    Write-Host 'השירות הוסר.' -ForegroundColor Green
}

Write-Host 'מסיר חוקי חומת אש...'
Get-NetFirewallRule -Name 'STG-App-*'     -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
Get-NetFirewallRule -Name 'STG-Website-*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
Write-Host 'חוקי חומת האש הוסרו.' -ForegroundColor Green

Write-Host 'מסיר חסימות הפעלה של דפדפנים (IFEO)...'
$ifeoRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'
if (Test-Path $ifeoRoot) {
    Get-ChildItem $ifeoRoot | ForEach-Object {
        $owner = (Get-ItemProperty -Path $_.PSPath -Name 'STGOwned' -ErrorAction SilentlyContinue).STGOwned
        if ($owner -eq 'ScreenTimeGuardian') {
            Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  הוסר: $($_.PSChildName)"
        }
    }
}
Write-Host 'חסימות ההפעלה הוסרו.' -ForegroundColor Green

Write-Host 'מסיר את הסוכן מההפעלה האוטומטית...'
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'ScreenTimeGuardianAgent' -ErrorAction SilentlyContinue

if (-not $KeepConfiguration) {
    $dataDirectory = 'C:\ProgramData\ScreenTimeGuardian'
    if (Test-Path $dataDirectory) {
        Remove-Item -Path $dataDirectory -Recurse -Force
        Write-Host 'תיקיית ההגדרות הוסרה.' -ForegroundColor Green
    }
}

Write-Host ''
Write-Host 'ההסרה הושלמה.' -ForegroundColor Green
