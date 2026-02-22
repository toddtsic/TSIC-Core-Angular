# ============================================================================
# Setup-IIS-Server.ps1 — Comprehensive IIS Setup for TSIC Application
# ============================================================================
# Creates app pools, IIS sites with HTTPS bindings (wildcard cert),
# SQL Server login for app pool identity, directory permissions, and firewall.
#
# Usage:
#   .\Setup-IIS-Server.ps1 -Environment Dev
#   .\Setup-IIS-Server.ps1 -Environment Prod
#   .\Setup-IIS-Server.ps1 -Environment Prod -BasePath "E:\" -SkipSql
#
# Prerequisites:
#   - Run as Administrator
#   - Wildcard cert (*.teamsportsinfo.com) installed in Local Machine cert store
#   - URL Rewrite module installed (script warns if missing)
# ============================================================================

#Requires -RunAsAdministrator

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Dev',

    [string]$ApiHostname,
    [string]$AngularHostname,
    [string]$BasePath,
    [string]$SqlInstance = '.\SS2016',
    [string]$DatabaseName = 'TSICV5',
    [string]$StaticsPath,
    [switch]$SkipSql,
    [switch]$SkipFirewall
)

# ---------------------------------------------------------------------------
# Environment Defaults
# ---------------------------------------------------------------------------
$envDefaults = @{
    Dev  = @{
        ApiHostname     = 'devapi.teamsportsinfo.com'
        AngularHostname = 'dev.teamsportsinfo.com'
        BasePath        = 'C:\'
        StaticsPath     = 'C:\Websites\TSIC-STATICS'
        ApiSiteName     = 'TSIC.Api'
        AngularSiteName = 'TSIC.App'
        ApiPoolName     = 'TSIC.Api'
        AngularPoolName = 'TSIC.App'
    }
    Prod = @{
        ApiHostname     = 'api.teamsportsinfo.com'
        AngularHostname = 'teamsportsinfo.com'
        BasePath        = 'D:\'
        StaticsPath     = 'E:\Websites\TSIC-STATICS'
        ApiSiteName     = 'TSIC-API-CP'
        AngularSiteName = 'TSIC-Angular-CP'
        ApiPoolName     = 'TSIC-API-Pool'
        AngularPoolName = 'TSIC-Angular-Pool'
    }
}

$defaults = $envDefaults[$Environment]

# Apply overrides or defaults
if (-not $ApiHostname)     { $ApiHostname     = $defaults.ApiHostname }
if (-not $AngularHostname) { $AngularHostname = $defaults.AngularHostname }
if (-not $BasePath)        { $BasePath        = $defaults.BasePath }
if (-not $StaticsPath)     { $StaticsPath     = $defaults.StaticsPath }

$ApiSiteName     = $defaults.ApiSiteName
$AngularSiteName = $defaults.AngularSiteName
$ApiPoolName     = $defaults.ApiPoolName
$AngularPoolName = $defaults.AngularPoolName

$ApiPath     = Join-Path $BasePath "Websites\$ApiSiteName"
$AngularPath = Join-Path $BasePath "Websites\$AngularSiteName"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  TSIC IIS Server Setup — $Environment Environment" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API Site:       $ApiSiteName" -ForegroundColor White
Write-Host "  API Pool:       $ApiPoolName" -ForegroundColor White
Write-Host "  API Path:       $ApiPath" -ForegroundColor White
Write-Host "  API Hostname:   $ApiHostname" -ForegroundColor White
Write-Host ""
Write-Host "  Angular Site:   $AngularSiteName" -ForegroundColor White
Write-Host "  Angular Pool:   $AngularPoolName" -ForegroundColor White
Write-Host "  Angular Path:   $AngularPath" -ForegroundColor White
Write-Host "  Angular Host:   $AngularHostname" -ForegroundColor White
Write-Host ""
Write-Host "  Statics Path:   $StaticsPath" -ForegroundColor White
Write-Host "  SQL Instance:   $SqlInstance" -ForegroundColor White
Write-Host "  Database:       $DatabaseName" -ForegroundColor White
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Install IIS Features
# ---------------------------------------------------------------------------
Write-Host "[1/7] Installing IIS features..." -ForegroundColor Green

$features = @(
    'IIS-WebServerRole',
    'IIS-WebServer',
    'IIS-CommonHttpFeatures',
    'IIS-HttpErrors',
    'IIS-HttpLogging',
    'IIS-RequestFiltering',
    'IIS-StaticContent',
    'IIS-DefaultDocument',
    'IIS-WebServerManagementTools',
    'IIS-ManagementConsole',
    'IIS-NetFxExtensibility45',
    'IIS-ASPNET45',
    'IIS-WebSockets',
    'IIS-HttpCompressionStatic',
    'IIS-HttpCompressionDynamic'
)

foreach ($feature in $features) {
    $state = Get-WindowsOptionalFeature -Online -FeatureName $feature -ErrorAction SilentlyContinue
    if ($state -and $state.State -eq 'Enabled') {
        # Already enabled, skip silently
    }
    elseif ($state) {
        Write-Host "  Enabling $feature..." -ForegroundColor Yellow
        Enable-WindowsOptionalFeature -Online -FeatureName $feature -NoRestart -ErrorAction SilentlyContinue | Out-Null
    }
    else {
        Write-Host "  Feature not available: $feature (skipping)" -ForegroundColor DarkGray
    }
}
Write-Host "  IIS features ready." -ForegroundColor Green

# Check URL Rewrite module
$urlRewriteKey = "HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite"
if (Test-Path $urlRewriteKey) {
    Write-Host "  URL Rewrite module: installed" -ForegroundColor Green
}
else {
    Write-Host "  WARNING: URL Rewrite module not detected." -ForegroundColor Yellow
    Write-Host "  Download from: https://www.iis.net/downloads/microsoft/url-rewrite" -ForegroundColor Yellow
    Write-Host "  The Angular site requires this module for SPA routing." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 2. Find Wildcard Certificate
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[2/7] Locating wildcard certificate..." -ForegroundColor Green

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

# ---------------------------------------------------------------------------
# 3. Create Application Pools
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[3/7] Creating application pools..." -ForegroundColor Green

Import-Module WebAdministration -ErrorAction Stop

foreach ($poolName in @($ApiPoolName, $AngularPoolName)) {
    $poolPath = "IIS:\AppPools\$poolName"
    if (Test-Path $poolPath) {
        Write-Host "  App pool '$poolName' already exists." -ForegroundColor DarkGray
    }
    else {
        New-WebAppPool -Name $poolName -Force | Out-Null
        Write-Host "  Created app pool '$poolName'." -ForegroundColor Green
    }

    # Configure: ApplicationPoolIdentity, No Managed Runtime (.NET Core), 64-bit
    Set-ItemProperty $poolPath -Name processModel.identityType -Value 4  # ApplicationPoolIdentity
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ""     # No managed runtime
    Set-ItemProperty $poolPath -Name enable32BitAppOnWin64 -Value $false # 64-bit
    Write-Host "  Configured '$poolName': ApplicationPoolIdentity, No Managed Runtime, 64-bit" -ForegroundColor White
}

# ---------------------------------------------------------------------------
# 4. Create Directories
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[4/7] Creating directories..." -ForegroundColor Green

foreach ($dir in @($ApiPath, $AngularPath, $StaticsPath)) {
    if (Test-Path $dir) {
        Write-Host "  Directory exists: $dir" -ForegroundColor DarkGray
    }
    else {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Created: $dir" -ForegroundColor Green
    }
}

# Create API subdirectories that persist across deployments
foreach ($subdir in @('logs', 'keys')) {
    $subdirPath = Join-Path $ApiPath $subdir
    if (-not (Test-Path $subdirPath)) {
        New-Item -ItemType Directory -Path $subdirPath -Force | Out-Null
        Write-Host "  Created: $subdirPath" -ForegroundColor Green
    }
}

# Set permissions: IIS_IUSRS read/execute on site roots
foreach ($dir in @($ApiPath, $AngularPath)) {
    $acl = Get-Acl $dir
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "IIS_IUSRS", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $dir -AclObject $acl
    Write-Host "  Granted IIS_IUSRS ReadAndExecute on $dir" -ForegroundColor White
}

# Grant app pool identity write access to logs/ and keys/
foreach ($subdir in @('logs', 'keys')) {
    $subdirPath = Join-Path $ApiPath $subdir
    $acl = Get-Acl $subdirPath
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "IIS AppPool\$ApiPoolName", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $subdirPath -AclObject $acl
    Write-Host "  Granted '$ApiPoolName' Modify on $subdirPath" -ForegroundColor White
}

# Grant app pool identity write access to statics (image uploads)
$acl = Get-Acl $StaticsPath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "IIS AppPool\$ApiPoolName", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl -Path $StaticsPath -AclObject $acl
Write-Host "  Granted '$ApiPoolName' Modify on $StaticsPath" -ForegroundColor White

# ---------------------------------------------------------------------------
# 5. Create IIS Sites with HTTPS Bindings
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[5/7] Creating IIS sites with HTTPS bindings..." -ForegroundColor Green

$certHash = $cert.Thumbprint
$certStore = "My"

# --- API Site ---
if (Get-Website -Name $ApiSiteName -ErrorAction SilentlyContinue) {
    Write-Host "  Site '$ApiSiteName' already exists — updating configuration." -ForegroundColor DarkGray
    Set-ItemProperty "IIS:\Sites\$ApiSiteName" -Name physicalPath -Value $ApiPath
    Set-ItemProperty "IIS:\Sites\$ApiSiteName" -Name applicationPool -Value $ApiPoolName
}
else {
    # Create with HTTPS binding
    New-Website -Name $ApiSiteName `
        -PhysicalPath $ApiPath `
        -ApplicationPool $ApiPoolName `
        -Ssl `
        -Port 443 `
        -HostHeader $ApiHostname `
        -Force | Out-Null
    Write-Host "  Created site '$ApiSiteName'." -ForegroundColor Green
}

# Ensure HTTPS binding exists with correct cert
$apiBinding = Get-WebBinding -Name $ApiSiteName -Protocol "https" -Port 443 -HostHeader $ApiHostname -ErrorAction SilentlyContinue
if (-not $apiBinding) {
    New-WebBinding -Name $ApiSiteName -Protocol "https" -Port 443 -HostHeader $ApiHostname -SslFlags 1
    Write-Host "  Added HTTPS binding: https://$ApiHostname" -ForegroundColor Green
}
else {
    Write-Host "  HTTPS binding exists: https://$ApiHostname" -ForegroundColor DarkGray
}

# Bind the certificate (using netsh for SNI-enabled bindings)
$certBindCmd = "netsh http add sslcert hostnameport=${ApiHostname}:443 certhash=$certHash certstorename=$certStore appid='{4dc3e181-e14b-4a21-b022-59fc669b0914}'"
& cmd /c $certBindCmd 2>$null
Write-Host "  Bound certificate to https://${ApiHostname}:443" -ForegroundColor White

# --- Angular Site ---
if (Get-Website -Name $AngularSiteName -ErrorAction SilentlyContinue) {
    Write-Host "  Site '$AngularSiteName' already exists — updating configuration." -ForegroundColor DarkGray
    Set-ItemProperty "IIS:\Sites\$AngularSiteName" -Name physicalPath -Value $AngularPath
    Set-ItemProperty "IIS:\Sites\$AngularSiteName" -Name applicationPool -Value $AngularPoolName
}
else {
    # Create with HTTPS binding
    New-Website -Name $AngularSiteName `
        -PhysicalPath $AngularPath `
        -ApplicationPool $AngularPoolName `
        -Ssl `
        -Port 443 `
        -HostHeader $AngularHostname `
        -Force | Out-Null
    Write-Host "  Created site '$AngularSiteName'." -ForegroundColor Green
}

# Ensure HTTPS binding with cert
$angularBinding = Get-WebBinding -Name $AngularSiteName -Protocol "https" -Port 443 -HostHeader $AngularHostname -ErrorAction SilentlyContinue
if (-not $angularBinding) {
    New-WebBinding -Name $AngularSiteName -Protocol "https" -Port 443 -HostHeader $AngularHostname -SslFlags 1
    Write-Host "  Added HTTPS binding: https://$AngularHostname" -ForegroundColor Green
}
else {
    Write-Host "  HTTPS binding exists: https://$AngularHostname" -ForegroundColor DarkGray
}

$certBindCmd = "netsh http add sslcert hostnameport=${AngularHostname}:443 certhash=$certHash certstorename=$certStore appid='{4dc3e181-e14b-4a21-b022-59fc669b0915}'"
& cmd /c $certBindCmd 2>$null
Write-Host "  Bound certificate to https://${AngularHostname}:443" -ForegroundColor White

# Add HTTP binding on port 80 for Angular (web.config redirects to HTTPS)
$httpBinding = Get-WebBinding -Name $AngularSiteName -Protocol "http" -Port 80 -HostHeader $AngularHostname -ErrorAction SilentlyContinue
if (-not $httpBinding) {
    New-WebBinding -Name $AngularSiteName -Protocol "http" -Port 80 -HostHeader $AngularHostname
    Write-Host "  Added HTTP binding: http://$AngularHostname (redirects to HTTPS via web.config)" -ForegroundColor Green
}
else {
    Write-Host "  HTTP binding exists: http://$AngularHostname" -ForegroundColor DarkGray
}

# Start sites
Start-Website -Name $ApiSiteName -ErrorAction SilentlyContinue
Start-Website -Name $AngularSiteName -ErrorAction SilentlyContinue
Write-Host "  Sites started." -ForegroundColor Green

# ---------------------------------------------------------------------------
# 6. SQL Server Login for App Pool Identity
# ---------------------------------------------------------------------------
Write-Host ""
if ($SkipSql) {
    Write-Host "[6/7] SQL Server login creation — SKIPPED (-SkipSql)" -ForegroundColor Yellow
}
else {
    Write-Host "[6/7] Creating SQL Server login for app pool identity..." -ForegroundColor Green

    $loginName = "IIS AppPool\$ApiPoolName"

    $sqlCheck = @"
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = '$loginName')
BEGIN
    CREATE LOGIN [$loginName] FROM WINDOWS;
    PRINT 'Created login: $loginName';
END
ELSE
    PRINT 'Login already exists: $loginName';

USE [$DatabaseName];

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = '$loginName')
BEGIN
    CREATE USER [$loginName] FOR LOGIN [$loginName];
    PRINT 'Created database user: $loginName';
END
ELSE
    PRINT 'Database user already exists: $loginName';

ALTER ROLE [db_datareader] ADD MEMBER [$loginName];
ALTER ROLE [db_datawriter] ADD MEMBER [$loginName];
PRINT 'Granted db_datareader and db_datawriter roles.';
"@

    # Try Invoke-Sqlcmd first, fall back to sqlcmd
    $sqlcmdAvailable = Get-Command sqlcmd -ErrorAction SilentlyContinue
    $invokeSqlAvailable = Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue

    if ($invokeSqlAvailable) {
        try {
            Invoke-Sqlcmd -ServerInstance $SqlInstance -Query $sqlCheck -TrustServerCertificate
            Write-Host "  SQL Server login configured via Invoke-Sqlcmd." -ForegroundColor Green
        }
        catch {
            Write-Host "  ERROR: Failed to create SQL login: $_" -ForegroundColor Red
            Write-Host "  You may need to run this manually in SSMS." -ForegroundColor Yellow
        }
    }
    elseif ($sqlcmdAvailable) {
        $tempSql = Join-Path $env:TEMP "tsic-setup-sql.sql"
        $sqlCheck | Out-File -FilePath $tempSql -Encoding UTF8
        try {
            sqlcmd -S $SqlInstance -i $tempSql -C
            Write-Host "  SQL Server login configured via sqlcmd." -ForegroundColor Green
        }
        catch {
            Write-Host "  ERROR: Failed to create SQL login: $_" -ForegroundColor Red
        }
        finally {
            Remove-Item $tempSql -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-Host "  WARNING: Neither Invoke-Sqlcmd nor sqlcmd found." -ForegroundColor Yellow
        Write-Host "  Run the following SQL manually in SSMS:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  CREATE LOGIN [$loginName] FROM WINDOWS;" -ForegroundColor White
        Write-Host "  USE [$DatabaseName];" -ForegroundColor White
        Write-Host "  CREATE USER [$loginName] FOR LOGIN [$loginName];" -ForegroundColor White
        Write-Host "  ALTER ROLE [db_datareader] ADD MEMBER [$loginName];" -ForegroundColor White
        Write-Host "  ALTER ROLE [db_datawriter] ADD MEMBER [$loginName];" -ForegroundColor White
    }
}

# ---------------------------------------------------------------------------
# 7. Firewall Rules
# ---------------------------------------------------------------------------
Write-Host ""
if ($SkipFirewall) {
    Write-Host "[7/7] Firewall rules — SKIPPED (-SkipFirewall)" -ForegroundColor Yellow
}
else {
    Write-Host "[7/7] Configuring firewall rules..." -ForegroundColor Green

    $rules = @(
        @{ Name = 'TSIC HTTP Inbound';  Port = 80  },
        @{ Name = 'TSIC HTTPS Inbound'; Port = 443 }
    )

    foreach ($rule in $rules) {
        $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
        if ($existing) {
            Write-Host "  Firewall rule exists: $($rule.Name)" -ForegroundColor DarkGray
        }
        else {
            New-NetFirewallRule -DisplayName $rule.Name `
                -Direction Inbound `
                -Protocol TCP `
                -LocalPort $rule.Port `
                -Action Allow | Out-Null
            Write-Host "  Created firewall rule: $($rule.Name) (TCP $($rule.Port))" -ForegroundColor Green
        }
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Setup Complete — $Environment Environment" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  API:     https://$ApiHostname" -ForegroundColor Green
Write-Host "  Angular: https://$AngularHostname" -ForegroundColor Green
Write-Host "  Angular: http://$AngularHostname (redirects to HTTPS)" -ForegroundColor Green
Write-Host ""
Write-Host "  App Pools:" -ForegroundColor White
Write-Host "    $ApiPoolName     — ApplicationPoolIdentity, 64-bit" -ForegroundColor White
Write-Host "    $AngularPoolName — ApplicationPoolIdentity, 64-bit" -ForegroundColor White
Write-Host ""
if (-not $SkipSql) {
    Write-Host "  SQL Login: [IIS AppPool\$ApiPoolName]" -ForegroundColor White
    Write-Host "  Database:  $DatabaseName ($SqlInstance)" -ForegroundColor White
    Write-Host ""
}
Write-Host "  Directories:" -ForegroundColor White
Write-Host "    API:     $ApiPath" -ForegroundColor White
Write-Host "    Angular: $AngularPath" -ForegroundColor White
Write-Host "    Statics: $StaticsPath" -ForegroundColor White
Write-Host ""
Write-Host "  Next Steps:" -ForegroundColor Yellow
Write-Host "    1. Deploy application files (use 1-Build-And-Deploy-Local.ps1 or deploy-to-server-template.ps1)" -ForegroundColor White
Write-Host "    2. Verify: https://$AngularHostname" -ForegroundColor White
Write-Host "    3. Verify: https://$ApiHostname/swagger" -ForegroundColor White
Write-Host ""
