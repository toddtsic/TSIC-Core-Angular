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

```powershell
# Run in elevated PowerShell on the server
$appPool = "TSIC.API"
$envVars = @{
  "AWS_ACCESS_KEY_ID"     = "<your_access_key>"
  "AWS_SECRET_ACCESS_KEY" = "<your_secret_key>"
  "AWS_REGION"            = "us-west-2"
  "VI_DEV_CLIENT_ID"      = "<vi_dev_client_id>"
  "VI_DEV_SECRET"         = "<vi_dev_secret>"
  "VI_PROD_CLIENT_ID"     = "<vi_prod_client_id>"
  "VI_PROD_SECRET"        = "<vi_prod_secret>"
  "ADN_SANDBOX_LOGINID"   = "<adn_sandbox_login>"
  "ADN_SANDBOX_TRANSACTIONKEY" = "<adn_sandbox_key>"
  "USLAX_API_BASE"        = "https://api.usalacrosse.com/"  # optional override
}

Import-Module WebAdministration
$envVars.GetEnumerator() | ForEach-Object {
  Write-Host "Setting $($_.Key) on app pool $appPool"
  # Remove existing entry if present (use double-quoted filter so $appPool expands)
  Remove-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' `
    -filter "system.applicationHost/applicationPools/add[@name='$appPool']/environmentVariables" `
    -name "add" -AtElement @{name=$_.Key} -ErrorAction SilentlyContinue
  # Add new value
  Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' `
    -filter "system.applicationHost/applicationPools/add[@name='$appPool']/environmentVariables" `
    -name "add" -value @{name=$_.Key; value=$_.Value}
}

# Recycle the pool to apply
Restart-WebAppPool -Name $appPool
```

Notes:
- Environment variables set at the app pool level are inherited by the worker process and picked up by the app without code changes.
- Keep keys out of web.config and appsettings.*.
- For non-Windows hosting, set equivalent process/env variables in your service manager (systemd, container env, Azure App Service Settings, etc.).

## 2) Verify the app sees them
From the server (same user as the IIS worker process) you can run:
```powershell
[System.Environment]::GetEnvironmentVariable("AWS_ACCESS_KEY_ID", "Process")
[System.Environment]::GetEnvironmentVariable("AWS_REGION", "Process")
```
You can also add a temporary diagnostics endpoint to read env vars, but remove it after validation.

## 3) Local development reminder
Use user-secrets instead of IIS env vars when developing locally:
```powershell
cd C:\Users\Administrator\source\TSIC-Core-Angular\TSIC-Core-Angular

# Examples
 dotnet user-secrets set "AWS:AccessKey" "..." --project src/backend/TSIC.API/TSIC.API.csproj
 dotnet user-secrets set "AWS:SecretKey" "..." --project src/backend/TSIC.API/TSIC.API.csproj
 dotnet user-secrets set "AWS:Region"    "us-west-2" --project src/backend/TSIC.API/TSIC.API.csproj
```
