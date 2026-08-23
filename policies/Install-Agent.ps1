<#
    Registers the user-session agent to start at logon for EVERY user.
    The agent shows the countdown warnings before a block begins; the service in
    session 0 cannot display anything to a logged in user.

    Run from an ELEVATED PowerShell window.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AgentExecutable,
    [switch]$Remove
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'יש להריץ סקריפט זה מחלון PowerShell עם הרשאות מנהל.'
}

# HKLM Run applies to every user who logs in, which is what we want here.
$runKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$valueName = 'ScreenTimeGuardianAgent'

if ($Remove) {
    Remove-ItemProperty -Path $runKey -Name $valueName -ErrorAction SilentlyContinue
    Write-Host 'הסוכן הוסר מההפעלה האוטומטית.' -ForegroundColor Yellow
    return
}

$resolved = (Resolve-Path $AgentExecutable).Path
New-ItemProperty -Path $runKey -Name $valueName -Value ('"' + $resolved + '"') `
    -PropertyType String -Force | Out-Null

Write-Host "הסוכן נרשם להפעלה בכניסת כל משתמש:" -ForegroundColor Green
Write-Host "  $resolved"
Write-Host 'ההתראות יופיעו החל מהכניסה הבאה למערכת.'
