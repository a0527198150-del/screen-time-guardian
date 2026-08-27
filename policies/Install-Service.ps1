<#
    Screen Time Guardian - installer.
    Run once from an ELEVATED PowerShell window (Run as administrator).
#>
param(
    [Parameter(Mandatory = $true)] [string]$ServiceExecutable,
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
New-Item -ItemType Directory -Force -Path $DataDirectory, $runtimeDirectory | Out-Null

function Remove-ExistingService {
    param([Parameter(Mandatory)] [string]$Name)

    $existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $existing) { return }

    Write-Host 'מסיר התקנה קודמת...'
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $Name -Force -ErrorAction Stop
    }

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $state = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if (-not $state -or $state.Status -eq 'Stopped') { break }
        Start-Sleep -Milliseconds 500
    }

    & sc.exe delete $Name | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "לא ניתן להסיר את השירות הקיים '$Name' (sc.exe exit code $LASTEXITCODE)."
    }

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        if (-not (Get-Service -Name $Name -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Milliseconds 500
    }

    throw "השירות '$Name' עדיין קיים לאחר ניסיון ההסרה. ההתקנה נעצרה כדי למנוע התקנה חלקית."
}

# Config folder ACL
$acl = Get-Acl $DataDirectory
$acl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($acl.Access)) { $acl.RemoveAccessRuleSpecific($rule) | Out-Null }
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new('SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new('Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new('Users', 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$administratorsSid = [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
$acl.SetOwner($administratorsSid)
Set-Acl -Path $DataDirectory -AclObject $acl

# Runtime ACL
$runtimeAcl = Get-Acl $runtimeDirectory
$runtimeAcl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($runtimeAcl.Access)) { $runtimeAcl.RemoveAccessRuleSpecific($rule) | Out-Null }
$runtimeAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new('SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$runtimeAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new('Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$runtimeAcl.SetOwner($administratorsSid)
Set-Acl -Path $runtimeDirectory -AclObject $runtimeAcl

Remove-ExistingService -Name $ServiceName

New-Service -Name $ServiceName `
    -BinaryPathName ('"' + $resolvedExecutable + '"') `
    -DisplayName $DisplayName `
    -Description 'שירות מדיניות זמן מסך' `
    -StartupType Automatic | Out-Null

sc.exe config $ServiceName start= delayed-auto | Out-Null
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
