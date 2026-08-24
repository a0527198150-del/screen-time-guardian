[CmdletBinding()]
param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$StartAfterInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'יש להריץ את Setup.ps1 כמנהל מערכת.'
}

$policies = Join-Path $PackageRoot 'Policies'
$service = Join-Path $PackageRoot 'Service\ScreenTimeGuardian.Service.exe'
$agent = Join-Path $PackageRoot 'Agent\ScreenTimeGuardian.Agent.exe'
$controlPanel = Join-Path $PackageRoot 'ControlPanel\ScreenTimeGuardian.ControlPanel.exe'

if (-not (Test-Path -LiteralPath $service -PathType Leaf)) {
    throw "קובץ השירות לא נמצא: $service"
}
if (-not (Test-Path -LiteralPath $controlPanel -PathType Leaf)) {
    throw "לוח הבקרה לא נמצא: $controlPanel"
}
if (-not (Test-Path -LiteralPath (Join-Path $policies 'Install-Service.ps1') -PathType Leaf)) {
    throw "סקריפט התקנת השירות לא נמצא: $policies"
}

& (Join-Path $policies 'Install-Service.ps1') -ServiceExecutable $service -StartAfterInstall:$StartAfterInstall

if (Test-Path -LiteralPath $agent -PathType Leaf) {
    & (Join-Path $policies 'Install-Agent.ps1') -AgentExecutable $agent
}

Write-Host 'ההתקנה הושלמה. פתח את ControlPanel כדי להגדיר סיסמה וכללים.' -ForegroundColor Green
Write-Host "לוח הבקרה: $controlPanel"
