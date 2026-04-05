# ============================================================================
# 04-Create-IIS-Sites.ps1 — Create IIS sites with HTTPS bindings
# ============================================================================
# Requires: wildcard cert (*.teamsportsinfo.com) in LocalMachine\My store.
# Creates API site (HTTPS 443) and Angular site (HTTPS 443 + HTTP 80).
# HTTP 80 on Angular is for redirect to HTTPS (handled by web.config).
# ============================================================================

#Requires -RunAsAdministrator

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Dev'
)

. "$PSScriptRoot\..\_config.ps1" -Environment $Environment

Write-Host ""
Write-Host "[Step 4] Creating IIS sites with HTTPS bindings ($Environment)..." -ForegroundColor Green

Import-Module WebAdministration -ErrorAction Stop

# --- Find Wildcard Certificate ---
$cert = Get-ChildItem Cert:\LocalMachine\My |
    Where-Object { $_.Subject -like '*teamsportsinfo.com*' -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($cert) {
    Write-Host "  Found cert: $($cert.Subject)" -ForegroundColor Green
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor White
    Write-Host "  Expires:    $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor White
}
else {
    Write-Host "  ERROR: No valid *.teamsportsinfo.com certificate found in LocalMachine\My" -ForegroundColor Red
    Write-Host "  Import your wildcard cert before running this script." -ForegroundColor Red
    exit 1
}

$certHash = $cert.Thumbprint
$certStore = "My"

# --- API Site ---
$apiSite = $Config.ApiSiteName
$apiPool = $Config.ApiPoolName
$apiPath = $Config.ApiPath
$apiHost = $Config.ApiHostname

if (Get-Website -Name $apiSite -ErrorAction SilentlyContinue) {
    Write-Host "  Site '$apiSite' already exists - updating configuration." -ForegroundColor DarkGray
    Set-ItemProperty "IIS:\Sites\$apiSite" -Name physicalPath -Value $apiPath
    Set-ItemProperty "IIS:\Sites\$apiSite" -Name applicationPool -Value $apiPool
}
else {
    New-Website -Name $apiSite `
        -PhysicalPath $apiPath `
        -ApplicationPool $apiPool `
        -Ssl `
        -Port 443 `
        -HostHeader $apiHost `
        -Force | Out-Null
    Write-Host "  Created site '$apiSite'." -ForegroundColor Green
}

# Ensure HTTPS binding exists with correct cert
$apiBinding = Get-WebBinding -Name $apiSite -Protocol "https" -Port 443 -HostHeader $apiHost -ErrorAction SilentlyContinue
if (-not $apiBinding) {
    New-WebBinding -Name $apiSite -Protocol "https" -Port 443 -HostHeader $apiHost -SslFlags 1
    Write-Host "  Added HTTPS binding: https://$apiHost" -ForegroundColor Green
}
else {
    Write-Host "  HTTPS binding exists: https://$apiHost" -ForegroundColor DarkGray
}

# Bind the certificate (using netsh for SNI-enabled bindings)
$certBindCmd = "netsh http add sslcert hostnameport=${apiHost}:443 certhash=$certHash certstorename=$certStore appid='{4dc3e181-e14b-4a21-b022-59fc669b0914}'"
& cmd /c $certBindCmd 2>$null
Write-Host "  Bound certificate to https://${apiHost}:443" -ForegroundColor White

# --- Angular Site ---
$angSite = $Config.AngularSiteName
$angPool = $Config.AngularPoolName
$angPath = $Config.AngularPath
$angHost = $Config.AngularHostname

if (Get-Website -Name $angSite -ErrorAction SilentlyContinue) {
    Write-Host "  Site '$angSite' already exists - updating configuration." -ForegroundColor DarkGray
    Set-ItemProperty "IIS:\Sites\$angSite" -Name physicalPath -Value $angPath
    Set-ItemProperty "IIS:\Sites\$angSite" -Name applicationPool -Value $angPool
}
else {
    New-Website -Name $angSite `
        -PhysicalPath $angPath `
        -ApplicationPool $angPool `
        -Ssl `
        -Port 443 `
        -HostHeader $angHost `
        -Force | Out-Null
    Write-Host "  Created site '$angSite'." -ForegroundColor Green
}

# Ensure HTTPS binding with cert
$angBinding = Get-WebBinding -Name $angSite -Protocol "https" -Port 443 -HostHeader $angHost -ErrorAction SilentlyContinue
if (-not $angBinding) {
    New-WebBinding -Name $angSite -Protocol "https" -Port 443 -HostHeader $angHost -SslFlags 1
    Write-Host "  Added HTTPS binding: https://$angHost" -ForegroundColor Green
}
else {
    Write-Host "  HTTPS binding exists: https://$angHost" -ForegroundColor DarkGray
}

$certBindCmd = "netsh http add sslcert hostnameport=${angHost}:443 certhash=$certHash certstorename=$certStore appid='{4dc3e181-e14b-4a21-b022-59fc669b0915}'"
& cmd /c $certBindCmd 2>$null
Write-Host "  Bound certificate to https://${angHost}:443" -ForegroundColor White

# Add HTTP binding on port 80 for Angular (web.config redirects to HTTPS)
$httpBinding = Get-WebBinding -Name $angSite -Protocol "http" -Port 80 -HostHeader $angHost -ErrorAction SilentlyContinue
if (-not $httpBinding) {
    New-WebBinding -Name $angSite -Protocol "http" -Port 80 -HostHeader $angHost
    Write-Host "  Added HTTP binding: http://$angHost (redirects to HTTPS via web.config)" -ForegroundColor Green
}
else {
    Write-Host "  HTTP binding exists: http://$angHost" -ForegroundColor DarkGray
}

# Start sites
Start-Website -Name $apiSite -ErrorAction SilentlyContinue
Start-Website -Name $angSite -ErrorAction SilentlyContinue
Write-Host "  Sites started." -ForegroundColor Green

Write-Host "[Step 4] Complete." -ForegroundColor Green
