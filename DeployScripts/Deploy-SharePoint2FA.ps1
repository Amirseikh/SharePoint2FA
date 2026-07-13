<#
.SYNOPSIS
    Deploys SharePoint2FA to a SharePoint 2019 / SE WFE server.

.PARAMETER DllPath
    Path to the compiled SharePoint2FA.dll (Release build).

.PARAMETER WebAppPort
    IIS port of your SharePoint web application. Default: 80

.EXAMPLE
    .\Deploy-SharePoint2FA.ps1 -DllPath ".\SharePoint2FA\bin\Release\SharePoint2FA.dll" -WebAppPort 80
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$DllPath,
    [string]$WebAppPort = "80"
)

$ErrorActionPreference = "Stop"

$LayoutsDest = "C:\Program Files\Common Files\Microsoft Shared\Web Server Extensions\16\TEMPLATE\LAYOUTS\TwoFactor"
$BinDest     = "C:\inetpub\wwwroot\wss\VirtualDirectories\$WebAppPort\bin"
$SnUtil      = "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\sn.exe"
$AspxSource  = Join-Path $PSScriptRoot "..\SharePoint2FA\Layouts\TwoFactor"

Write-Host "`nSharePoint2FA — Deployment" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan

# ── 1. Public key token ───────────────────────────────────────────────────────
Write-Host "`n[1/4] Reading public key token..." -ForegroundColor Yellow
if (-not (Test-Path $DllPath))  { throw "DLL not found: $DllPath. Build in Release mode first." }
if (-not (Test-Path $SnUtil))   { throw "sn.exe not found. Install Windows SDK." }

$snOut     = & $SnUtil -T $DllPath 2>&1
$tokenLine = $snOut | Where-Object { $_ -match "Public key token is" }
$token     = ($tokenLine -replace ".*Public key token is\s+", "").Trim()
Write-Host "  ✔ Token: $token" -ForegroundColor Green
Write-Host "  → Update PublicKeyToken in web.config with this value" -ForegroundColor Magenta

# ── 2. Copy DLL to bin ────────────────────────────────────────────────────────
Write-Host "`n[2/4] Copying DLL to web app bin..." -ForegroundColor Yellow
if (-not (Test-Path $BinDest)) { throw "Bin folder not found: $BinDest" }
Copy-Item $DllPath $BinDest -Force
Write-Host "  ✔ Copied to: $BinDest" -ForegroundColor Green

# ── 3. Copy ASPX + CSS to layouts hive ───────────────────────────────────────
Write-Host "`n[3/4] Copying ASPX and CSS to layouts hive..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $LayoutsDest -Force | Out-Null
Copy-Item "$AspxSource\TwoFactorAuth.aspx" $LayoutsDest -Force
Copy-Item "$AspxSource\TwoFactorAuth.css"  $LayoutsDest -Force
Write-Host "  ✔ Copied to: $LayoutsDest" -ForegroundColor Green

# ── 4. Register Windows Event Log source ─────────────────────────────────────
Write-Host "`n[4/4] Registering Event Log source 'SP2FA'..." -ForegroundColor Yellow
if (-not [System.Diagnostics.EventLog]::SourceExists("SP2FA")) {
    [System.Diagnostics.EventLog]::CreateEventSource("SP2FA", "Application")
    Write-Host "  ✔ Event source registered" -ForegroundColor Green
} else {
    Write-Host "  ✔ Event source already exists" -ForegroundColor Green
}

# ── IIS Reset ─────────────────────────────────────────────────────────────────
Write-Host "`nRestarting IIS..." -ForegroundColor Yellow
iisreset /noforce
Write-Host "  ✔ IIS restarted" -ForegroundColor Green

Write-Host "`n=================================" -ForegroundColor Cyan
Write-Host "Deployment complete!" -ForegroundColor Green
Write-Host "`nNEXT: Update web.config with these values:" -ForegroundColor Yellow
Write-Host "  PublicKeyToken = $token" -ForegroundColor Magenta
Write-Host "  SP2FA:ADDomain, SP2FA:SmsApiUrl, SP2FA:SmsApiKey" -ForegroundColor Magenta
Write-Host "`nThen apply web.config changes and run iisreset /noforce" -ForegroundColor Yellow
