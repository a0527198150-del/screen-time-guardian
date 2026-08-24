<#
    Screen Time Guardian - installer.
    Run once from an ELEVATED PowerShell window (Run as administrator).

    Key differences from the previous installer:
      * Delayed automatic start, so enforcement never begins during early boot.
      * NO automatic restart on failure - this is what turned a single crash into a reboot loop.
      * A runtime folder writable only by SYSTEM and Administrators.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceExecutable,
    [string]$ServiceName = 'ScreenTimeGuardian',
    [string]$DisplayName = 'Screen Time Guardian',
    [string]$DataDirectory = 'C:\ProgramData\ScreenTimeGuardian',
    [switch]$StartAfterInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$currentPrincipal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'יש להריץ סקריפט זה מחלון PowerShell עם הרשאות מנהל.'
}

$resolvedExecutable = (Resolve-Path $ServiceExecutable).Path
$runtimeDirectory = Join-Path $DataDirectory 'runtime'

New-Item -ItemType Directory -Force -Path $DataDirectory  | Out-Null
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null

# Config folder: users may read (the native host runs as a standard user), never write.
$acl = Get-Acl $DataDirectory
$acl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($acl.Access)) {
    $acl.RemoveAccessRuleSpecific($rule) | Out-Null
}
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    'SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    'Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    'Users', 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$administratorsSid = [Security.Principal.SecurityIdentifier]::new(
    [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
$acl.SetOwner($administratorsSid)
Set-Acl -Path $DataDirectory -AclObject $acl

# Runtime folder holds the crash marker and the safe mode flag. Users get no access at all.
$runtimeAcl = Get-Acl $runtimeDirectory
$runtimeAcl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($runtimeAcl.Access)) {
    $runtimeAcl.RemoveAccessRuleSpecific($rule) | Out-Null
}
$runtimeAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    'SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$runtimeAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    'Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$runtimeAcl.SetOwner($administratorsSid)
Set-Acl -Path $runtimeDirectory -AclObject $runtimeAcl

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'מסיר התקנה קודמת...'
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 3
}

New-Service -Name $ServiceName `
    -BinaryPathName ('"' + $resolvedExecutable + '"') `
    -DisplayName $DisplayName `
    -Description 'שירות מדיניות זמן מסך' `
    -StartupType Automatic | Out-Null

# Delayed start: Windows finishes booting before this service does anything.
sc.exe config $ServiceName start= delayed-auto | Out-Null

# CRITICAL: no automatic restart. If the service dies, it stays down until a human
# starts it. A service that revives itself after crashing the machine is a reboot loop.
sc.exe failure $ServiceName reset= 0 actions= '' | Out-Null

Write-Host ''
Write-Host "השירות $ServiceName הותקן." -ForegroundColor Green
Write-Host 'הפעלה מושהית מופעלת, והפעלה אוטומטית מחדש לאחר כשל מבוטלת.'
Write-Host ''
Write-Host 'מתג חירום: צור קובץ ריק בשם SAFEMODE בתיקייה'
Write-Host "  $DataDirectory"
Write-Host 'כדי לעצור מיד כל אכיפה, בלי להסיר את התוכנה.'

if ($StartAfterInstall) {
    Start-Service -Name $ServiceName
    Write-Host ''
    Write-Host 'השירות הופעל. האכיפה תתחיל רק לאחר תקופת החסד.' -ForegroundColor Green
}
