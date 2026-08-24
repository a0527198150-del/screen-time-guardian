<#
Screen Time Guardian - website blocking diagnostics.
Run from an elevated PowerShell prompt.
#>

$ErrorActionPreference = 'Continue'

Write-Host "=== 1. Configuration ===" -ForegroundColor Cyan
$cfg = 'C:\ProgramData\ScreenTimeGuardian\config.json'
if (Test-Path $cfg) {
    try {
        $json = Get-Content $cfg -Raw | ConvertFrom-Json
        Write-Host ("WebsiteEnforcement          : " + $json.websiteEnforcement)
        Write-Host ("EnforceForAdministrators   : " + $json.enforceForAdministrators)
        Write-Host ("AllowMachineWideWebsiteBlocking: " + $json.allowMachineWideWebsiteBlocking)
        Write-Host ("Website rules configured    : " + @($json.websites | Where-Object { $null -ne $_ }).Count)
    }
    catch {
        Write-Host ("Configuration read failed: " + $_.Exception.Message) -ForegroundColor Red
    }
}
else {
    Write-Host "Configuration file not found." -ForegroundColor Red
}

Write-Host "`n=== 2. Safe mode ===" -ForegroundColor Cyan
$flag = 'C:\ProgramData\ScreenTimeGuardian\runtime\safemode.flag'
$kill = 'C:\ProgramData\ScreenTimeGuardian\SAFEMODE'
Write-Host ("safemode.flag present : " + (Test-Path $flag))
Write-Host ("SAFEMODE kill switch  : " + (Test-Path $kill))
if (Test-Path $flag) {
    Get-Content $flag
}

Write-Host "`n=== 3. Defender / Network Protection ===" -ForegroundColor Cyan
try {
    Get-MpComputerStatus |
        Select-Object AMServiceEnabled, RealTimeProtectionEnabled, AMProductVersion |
        Format-List

    Write-Host ("EnableNetworkProtection: " + (Get-MpPreference).EnableNetworkProtection)
    Write-Host "Values: 0 = disabled, 1 = block, 2 = audit."
    Write-Host "FQDN rules require Defender platform version 4.18.2209.7 or later."
}
catch {
    Write-Host ("Defender query failed: " + $_.Exception.Message) -ForegroundColor Red
}

Write-Host "`n=== 4. Firewall rules created by the service ===" -ForegroundColor Cyan
$rules = Get-NetFirewallRule -Name 'STG-Website-*' -ErrorAction SilentlyContinue
if ($rules) {
    $rules |
        Select-Object Name, DisplayName, Enabled, Action, Direction |
        Format-Table -AutoSize
}
else {
    Write-Host "No STG-Website-* rules exist. The service is not creating them." -ForegroundColor Yellow
}

Write-Host "`n=== 5. Dynamic keyword resolution ===" -ForegroundColor Cyan
try {
    $keywords = Get-NetFirewallDynamicKeywordAddress -AllAutoResolve -ErrorAction Stop
    if ($keywords) {
        $keywords |
            Select-Object Id, Keyword, Addresses |
            Format-Table -AutoSize
        Write-Host "Empty Addresses means the keyword never resolved; the rule is not enforced."
    }
    else {
        Write-Host "No AutoResolve keywords registered." -ForegroundColor Yellow
    }
}
catch {
    Write-Host ("Dynamic keyword cmdlets unavailable: " + $_.Exception.Message) -ForegroundColor Red
}

Write-Host "`n=== 6. Proxy configuration (Netfree interaction) ===" -ForegroundColor Cyan
$ie = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue
Write-Host ("ProxyEnable : " + $ie.ProxyEnable)
Write-Host ("ProxyServer : " + $ie.ProxyServer)
Write-Host ("AutoConfigURL: " + $ie.AutoConfigURL)
Write-Host "If a proxy is in use, the browser connects to the proxy IP, not the site IP."
Write-Host "IP-based firewall rules cannot block sites in that configuration."

Write-Host "`n=== 7. Recent service log entries ===" -ForegroundColor Cyan
try {
    Get-EventLog -LogName Application -Source 'Screen Time Guardian' -Newest 30 -ErrorAction Stop |
        Select-Object TimeGenerated, EntryType, Message |
        Format-List
}
catch {
    Write-Host "No event log entries found for source 'Screen Time Guardian'."
}
