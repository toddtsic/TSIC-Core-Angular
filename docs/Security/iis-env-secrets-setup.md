# IIS Environment Variables for Secrets

Use IIS app pool environment variables so secrets never live in appsettings or web.config. Apply on the server hosting TSIC API.

## 1) Set variables on the App Pool
Replace `TSIC-API` with your app pool name. APPPOOL SHOULD BE TSIC.API

Value mapping (user-secrets ➜ IIS env var):
- AWS:AccessKey ➜ AWS_ACCESS_KEY_ID
- AWS:SecretKey ➜ AWS_SECRET_ACCESS_KEY
- AWS:Region ➜ AWS_REGION
- VerticalInsure:DevClientId ➜ VI_DEV_CLIENT_ID
- VerticalInsure:DevSecret ➜ VI_DEV_SECRET
- VerticalInsure:ProdClientId ➜ VI_PROD_CLIENT_ID
- VerticalInsure:ProdSecret ➜ VI_PROD_SECRET
- AuthorizeNet:SandboxLoginId ➜ ADN_SANDBOX_LOGINID
- AuthorizeNet:SandboxTransactionKey ➜ ADN_SANDBOX_TRANSACTIONKEY

### Server apply (no user-secrets needed)
Run this on the IIS server (fill the values first). This does **not** read user-secrets.

**Prerequisites**: 
- Run PowerShell **as Administrator**
- IIS app pool named `TSIC.API` must already exist
- .NET 10.0 Runtime must be installed

```powershell
# ===== START COPY =====
#Requires -RunAsAdministrator

# =============================================================================
# STEP 1: Fill in your production secrets below
# =============================================================================
$appPool = "TSIC.API"
$envVars = @{
  "AWS_ACCESS_KEY_ID"     = "AKIAIOSFODNN7EXAMPLE"
  "AWS_SECRET_ACCESS_KEY" = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
  "AWS_REGION"            = "us-west-2"
  "VI_DEV_CLIENT_ID"      = "test_XXXXXXXXXXXXXXXXXXXXXXXXXXXX"
  "VI_DEV_SECRET"         = "test_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
  "VI_PROD_CLIENT_ID"     = "live_XXXXXXXXXXXXXXXXXXXXXXXXXXXX"
  "VI_PROD_SECRET"        = "live_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX"
  "ADN_SANDBOX_LOGINID"   = "XXXXXXXXXXXX"
  "ADN_SANDBOX_TRANSACTIONKEY" = "XXXXXXXXXXXX"
  "USLAX_API_BASE"        = "https://api.usalacrosse.com/"
}

# =============================================================================
# STEP 2: Run the script (no edits needed below this line)
# =============================================================================
$ErrorActionPreference = "Stop"

Write-Host "IIS App Pool Environment Variable Setup" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# Check prerequisites
try {
  Import-Module WebAdministration -ErrorAction Stop
  Write-Host "[OK] WebAdministration module loaded" -ForegroundColor Green
} catch {
  Write-Error "FAILED: WebAdministration module not available. Install IIS Management Scripts feature."
  exit 1
}

# Verify app pool exists
if (-not (Test-Path "IIS:\AppPools\$appPool")) {
  Write-Error "FAILED: App pool '$appPool' does not exist in IIS."
  exit 1
}
Write-Host "[OK] App pool '$appPool' exists" -ForegroundColor Green

# Verify you replaced placeholder values
if ($envVars.Values -match "YOUR_") {
  Write-Error "FAILED: You must replace placeholder values (YOUR_*) with real secrets before running this script."
  exit 1
}
Write-Host "[OK] No placeholder values detected" -ForegroundColor Green

# Apply environment variables using appcmd.exe (most reliable method)
Write-Host "`nApplying $($envVars.Count) environment variables..." -ForegroundColor Yellow

$appcmd = "$env:SystemRoot\System32\inetsrv\appcmd.exe"
if (-not (Test-Path $appcmd)) {
  Write-Error "FAILED: appcmd.exe not found at $appcmd"
  exit 1
}

foreach ($kvp in $envVars.GetEnumerator()) {
  $key = $kvp.Key
  $value = $kvp.Value
  
  Write-Host "  Setting: $key" -ForegroundColor Gray
  
  try {
    # Clear any existing value first (ignore errors if doesn't exist)
    $clearResult = & $appcmd clear config -section:system.applicationHost/applicationPools `
      "/[name='$appPool'].environmentVariables.[name='$key']" 2>&1
    
    # Add the new value using proper syntax
    $addResult = & $appcmd set config -section:system.applicationHost/applicationPools `
      "/+[name='$appPool'].environmentVariables.[name='$key',value='$value']" 2>&1
    
    if ($LASTEXITCODE -ne 0) {
      Write-Warning "Failed to set ${key}: $addResult"
    }
  } catch {
    Write-Warning "Failed to set ${key}: $_"
  }
}

Write-Host "`n[OK] Environment variables applied" -ForegroundColor Green

# Recycle the app pool
Write-Host "`nRecycling app pool '$appPool'..." -ForegroundColor Yellow
Restart-WebAppPool -Name $appPool
Start-Sleep -Seconds 2

Write-Host "[OK] App pool recycled" -ForegroundColor Green

# Verify settings
Write-Host "`nVerifying configuration..." -ForegroundColor Yellow

# Read environment variables directly from IIS configuration
$configPath = "IIS:\AppPools\$appPool"
$envVarCollection = Get-ItemProperty $configPath -Name environmentVariables -ErrorAction SilentlyContinue

if ($null -eq $envVarCollection -or $null -eq $envVarCollection.Collection) {
  Write-Warning "Could not read environment variables collection. Variables may still be set correctly."
  Write-Host "Run the manual verification command in section 2 to confirm." -ForegroundColor Yellow
} else {
  $appliedVarNames = @()
  foreach ($item in $envVarCollection.Collection) {
    if ($item.name) {
      $appliedVarNames += $item.name
    }
  }

  $missing = @()
  foreach ($key in $envVars.Keys) {
    if ($appliedVarNames -notcontains $key) {
      $missing += $key
    }
  }

  if ($missing.Count -eq 0) {
    Write-Host "[OK] All $($envVars.Count) variables verified in IIS configuration" -ForegroundColor Green
    Write-Host "`nApplied variables:" -ForegroundColor Cyan
    $appliedVarNames | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
  } else {
    Write-Warning "Some variables were not applied: $($missing -join ', ')"
    Write-Host "`nDiagnostics - All configured variables:" -ForegroundColor Yellow
    $appliedVarNames | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }
  }
}

Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "Setup complete!" -ForegroundColor Green
Write-Host "The API will read these values from environment variables on next request." -ForegroundColor Gray
# ===== END COPY =====
```

## 2) Verify the app sees them

The script above includes automatic verification. To manually check IIS configuration:

```powershell
# View all environment variables for the app pool
$appPool = "TSIC.API"
Import-Module WebAdministration
$envVars = Get-ItemProperty "IIS:\AppPools\$appPool" -Name environmentVariables -ErrorAction SilentlyContinue
if ($envVars -and $envVars.Collection) {
  $envVars.Collection | Format-Table name, value -AutoSize
} else {
  Write-Host "No environment variables found or unable to read collection."
}
```

To test that the API actually reads them (requires app to be running):
- Check application logs for successful AWS/VerticalInsure connections
- Use a temporary diagnostic endpoint (add to `Program.cs` for testing only):
  ```csharp
  app.MapGet("/api/debug/env", () => new {
      HasAWS = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")),
      HasVI = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VI_PROD_CLIENT_ID")),
      HasADN = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ADN_SANDBOX_LOGINID"))
  }).RequireAuthorization("SuperUserOnly");
  ```
  **Remove this endpoint after verification!**

## 3) Local development reminder
Use user-secrets instead of IIS env vars when developing locally:
```powershell
cd C:\Users\Administrator\source\TSIC-Core-Angular\TSIC-Core-Angular

# List all secrets
dotnet user-secrets list --project src/backend/TSIC.API/TSIC.API.csproj

# Set secrets (examples)
dotnet user-secrets set "AWS:AccessKey" "..." --project src/backend/TSIC.API/TSIC.API.csproj
dotnet user-secrets set "AWS:SecretKey" "..." --project src/backend/TSIC.API/TSIC.API.csproj
dotnet user-secrets set "AWS:Region"    "us-west-2" --project src/backend/TSIC.API/TSIC.API.csproj
```
