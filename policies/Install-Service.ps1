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
    throw 'This script must be run from an elevated Administrator PowerShell window.'
}

$resolvedExecutable = (Resolve-Path $ServiceExecutable).Path
New-Item -ItemType Directory -Force -Path $DataDirectory | Out-Null

$acl = Get-Acl $DataDirectory
$acl.SetAccessRuleProtection($true, $false)
$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    'SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    'Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    'Users', 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -Path $DataDirectory -AclObject $acl

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName `
    -BinaryPathName ('"' + $resolvedExecutable + '"') `
    -DisplayName $DisplayName `
    -Description 'Screen time policy service' `
    -StartupType Automatic | Out-Null

if ($StartAfterInstall) {
    Start-Service -Name $ServiceName
}

Write-Host "Service $ServiceName installed. No browser or firewall policy was changed by this script."
