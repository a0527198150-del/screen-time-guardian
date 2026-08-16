param(
    [string]$ServiceName = 'ScreenTimeGuardian',
    [switch]$RemoveData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$currentPrincipal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run from an elevated Administrator PowerShell window.'
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
    }
    sc.exe delete $ServiceName | Out-Null
}

if ($RemoveData) {
    Remove-Item 'C:\ProgramData\ScreenTimeGuardian' -Recurse -Force -ErrorAction Stop
    Write-Host 'Service and configuration data removed.'
}
else {
    Write-Host 'Service removed. Configuration data was preserved.'
}
