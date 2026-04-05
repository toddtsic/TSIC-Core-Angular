# ============================================================================
# 04-Create-IIS-Sites.ps1 — Create IIS sites with HTTPS bindings
# ============================================================================
# Requires: wildcard cert (*.teamsportsinfo.com) in LocalMachine\My store.
# Creates API site (HTTPS 443) and Angular site (HTTPS 443 + HTTP 80).
# HTTP 80 on Angular is for redirect to HTTPS (handled by web.config).
# ============================================================================

#Requires -RunAsAdministrator

. "$PSScriptRoot\..\_config.ps1"

# Capture all config values BEFORE importing WebAdministration (module side-effects)
$apiSite = $Config.ApiSiteName
$apiPool = $Config.ApiPoolName
$apiPath = $Config.ApiPath
$apiHost = $Config.ApiHostname
$angSite = $Config.AngularSiteName
$angPool = $Config.AngularPoolName
$angPath = $Config.AngularPath
$angHost = $Config.AngularHostname

foreach ($v in @('apiSite','apiPool','apiPath','apiHost','angSite','angPool','angPath','angHost')) {
    if (-not (Get-Variable $v -ValueOnly)) {
        throw "Config value '$v' is null. Check _config.ps1."
    }
}

Write-Host ""
Write-Host "[Step 4] Creating IIS sites with HTTPS bindings (Dev)..." -ForegroundColor Green

Import-Module WebAdministration -ErrorAction Stop

# --- Find Wildcard Certificate (check Personal and WebHosting stores) ---
$cert = $null
$certStore = $null
foreach ($store in @('My', 'WebHosting')) {
    $found = Get-ChildItem "Cert:\LocalMachine\$store" -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like '*teamsportsinfo.com*' -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if ($found) {
        $cert = $found
        $certStore = $store
        break
    }
}

if ($cert) {
    Write-Host "  Found cert: $($cert.Subject) (store: $certStore)" -ForegroundColor Green
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor White
    Write-Host "  Expires:    $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor White
}
else {
    Write-Host "  Checked stores: Personal (My) and WebHosting" -ForegroundColor Red
    throw "No valid *.teamsportsinfo.com certificate found in LocalMachine cert stores"
}

$certHash = $cert.Thumbprint

# --- API Site ---
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

# Bind the certificate to the HTTPS binding
try {
    $binding = Get-WebBinding -Name $apiSite -Protocol "https" -Port 443 -HostHeader $apiHost
    $binding.AddSslCertificate($certHash, $certStore)
    Write-Host "  Bound certificate to https://${apiHost}:443 (store: $certStore)" -ForegroundColor Green
} catch {
    if ($_.Exception.Message -match 'already') {
        Write-Host "  Certificate already bound to https://${apiHost}:443" -ForegroundColor DarkGray
    } else {
        Write-Host "  WARNING: Certificate binding failed for https://${apiHost}:443 — $_" -ForegroundColor Yellow
    }
}

# --- Angular Site ---
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

# Bind the certificate to the HTTPS binding
try {
    $binding = Get-WebBinding -Name $angSite -Protocol "https" -Port 443 -HostHeader $angHost
    $binding.AddSslCertificate($certHash, $certStore)
    Write-Host "  Bound certificate to https://${angHost}:443 (store: $certStore)" -ForegroundColor Green
} catch {
    if ($_.Exception.Message -match 'already') {
        Write-Host "  Certificate already bound to https://${angHost}:443" -ForegroundColor DarkGray
    } else {
        Write-Host "  WARNING: Certificate binding failed for https://${angHost}:443 — $_" -ForegroundColor Yellow
    }
}

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
