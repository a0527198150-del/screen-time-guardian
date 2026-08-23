param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^[a-z]{32}$')]
    [string]$ExtensionId,
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^[a-z]{32}$')]
    [string]$EdgeExtensionId,
    [string]$NativeHostPath = 'C:\Program Files\ScreenTimeGuardian\NativeHost\ScreenTimeGuardian.NativeHost.exe',
    [string]$KeyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# If -KeyPath is provided, derive the extension ID from the .pem file.
if ($KeyPath) {
    if (-not (Test-Path -LiteralPath $KeyPath -PathType Leaf)) {
        throw "Key file not found: $KeyPath"
    }

    $rsa = [System.Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem((Get-Content -LiteralPath $KeyPath -Raw))
    }
    catch {
        throw "Could not read RSA key from '$KeyPath'. Make sure it is a valid PEM file."
    }

    $pubDer = $rsa.ExportSubjectPublicKeyInfo()
    $sha = [System.Security.Cryptography.SHA256]::Create().ComputeHash($pubDer)
    $id = -join ($sha[0..15] | ForEach-Object {
        [char]([int]'a' + ($_ -shr 4))
        [char]([int]'a' + ($_ -band 0x0f))
    })

    if (-not $ExtensionId) {
        $ExtensionId = $id
    }
    elseif ($ExtensionId -ne $id) {
        Write-Warning "Derived ID ($id) does not match -ExtensionId ($ExtensionId). Using -ExtensionId."
    }

    if (-not $EdgeExtensionId) {
        $EdgeExtensionId = $id
    }
    elseif ($EdgeExtensionId -ne $id) {
        Write-Warning "Derived ID ($id) does not match -EdgeExtensionId ($EdgeExtensionId). Using -EdgeExtensionId."
    }

    Write-Host "Derived extension ID: $id" -ForegroundColor Green
    $rsa.Dispose()
}

# After derivation, both IDs must be valid.
if ($ExtensionId -notmatch '^[a-z]{32}$') {
    throw 'ExtensionId must be the final 32-character Chrome extension id (or pass -KeyPath).'
}

if ($EdgeExtensionId -notmatch '^[a-z]{32}$') {
    throw 'EdgeExtensionId must be the final 32-character Edge extension id (or pass -KeyPath).'
}

if (-not (Test-Path -LiteralPath $NativeHostPath -PathType Leaf)) {
    throw "Native host executable was not found: $NativeHostPath"
}

$resolvedNativeHostPath = (Resolve-Path -LiteralPath $NativeHostPath).Path
$manifest = @{
    name = 'com.screentimeguardian.host'
    description = 'Screen Time Guardian policy bridge'
    path = $resolvedNativeHostPath
    type = 'stdio'
    allowed_origins = @(
        "chrome-extension://$ExtensionId/"
        "chrome-extension://$EdgeExtensionId/"
    )
} | ConvertTo-Json -Depth 4

$manifestPath = Join-Path $env:ProgramData 'ScreenTimeGuardian\com.screentimeguardian.host.json'
New-Item -ItemType Directory -Force -Path (Split-Path $manifestPath) | Out-Null
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifest,
    [System.Text.UTF8Encoding]::new($false))

$chromeKey = 'HKLM:\Software\Google\Chrome\NativeMessagingHosts\com.screentimeguardian.host'
$edgeKey = 'HKLM:\Software\Microsoft\Edge\NativeMessagingHosts\com.screentimeguardian.host'
New-Item -Path $chromeKey -Force | Out-Null
New-Item -Path $edgeKey -Force | Out-Null
Set-ItemProperty -Path $chromeKey -Name '(default)' -Value $manifestPath
Set-ItemProperty -Path $edgeKey -Name '(default)' -Value $manifestPath

Write-Host 'Native Messaging host registered for Chrome and Edge.' -ForegroundColor Green
Write-Host "Extension ID : $ExtensionId"
Write-Host "Edge ID      : $EdgeExtensionId"
Write-Host "Manifest     : $manifestPath"
Write-Host ''
Write-Host 'Restart Chrome and Edge to load the registration.'