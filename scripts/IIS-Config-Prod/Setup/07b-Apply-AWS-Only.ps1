# ============================================================================
# 07b-Apply-AWS-Only.ps1 — Add AWS_* env vars to claude-api app pool
# ============================================================================
# Self-contained, paste-and-run alternative to 07-Apply-Secrets.ps1 for the
# narrow case of adding the AWS SES credential trio without touching anything
# else on the pool.
#
# Use 07-Apply-Secrets.ps1 (the full script + secrets file) for first-time
# pool setup or rotating multiple secrets at once. Use this script when you
# just need to land or rotate the AWS_* trio.
#
# Edit the three values below, then either:
#   .\07b-Apply-AWS-Only.ps1                  (run as Admin)
# or paste the entire body into an Admin PowerShell window on TSIC-PHOENIX.
#
# Non-destructive: only the three AWS_* keys are touched. ADN_*, Anthropic__*,
# VI_*, USLAX_*, ASPNETCORE_ENVIRONMENT etc. are preserved.
# ============================================================================

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

# ── EDIT THESE THREE BEFORE RUNNING ─────────────────────────────────────────
$AwsAccessKeyId     = '<PASTE-AKIA-VALUE>'
$AwsSecretAccessKey = '<PASTE-SECRET-VALUE>'
$AwsRegion          = 'us-west-2'   # confirmed against legacy appsettings.json
# ────────────────────────────────────────────────────────────────────────────

$PoolName = 'claude-api'

$envVars = [ordered]@{
    'AWS_ACCESS_KEY_ID'     = $AwsAccessKeyId
    'AWS_SECRET_ACCESS_KEY' = $AwsSecretAccessKey
    'AWS_REGION'            = $AwsRegion
}

foreach ($kv in $envVars.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace($kv.Value) -or $kv.Value -like '<PASTE*') {
        throw "Refusing to apply: $($kv.Key) is empty or still a placeholder. Edit values at top."
    }
}

Import-Module WebAdministration -ErrorAction Stop

if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
    throw "App pool '$PoolName' does not exist on $env:COMPUTERNAME."
}

Write-Host ""
Write-Host "Host:  $env:COMPUTERNAME" -ForegroundColor Cyan
Write-Host "Pool:  $PoolName" -ForegroundColor Cyan
Write-Host "Vars:  $($envVars.Count) (AWS_* trio)" -ForegroundColor Cyan
Write-Host ""

$appcmd = "$env:SystemRoot\System32\inetsrv\appcmd.exe"

foreach ($kv in $envVars.GetEnumerator()) {
    $key = $kv.Key; $value = $kv.Value
    Write-Host "  Setting: $key" -ForegroundColor Gray
    & $appcmd clear config -section:system.applicationHost/applicationPools "/[name='$PoolName'].environmentVariables.[name='$key']" 2>&1 | Out-Null
    & $appcmd set   config -section:system.applicationHost/applicationPools "/+[name='$PoolName'].environmentVariables.[name='$key',value='$value']" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Warning "Failed to set $key" }
}

Write-Host ""
Write-Host "  Recycling app pool '$PoolName'..." -ForegroundColor Yellow
Restart-WebAppPool -Name $PoolName
Start-Sleep -Seconds 2
Write-Host "  Recycled." -ForegroundColor Green

Write-Host ""
Write-Host "  Pool '$PoolName' environment variables (post-apply):" -ForegroundColor Cyan
$collection = (Get-Item "IIS:\AppPools\$PoolName").environmentVariables.Collection
$sorted = @($collection | Sort-Object name)
foreach ($item in $sorted) {
    $val = if ($item.value) { "(set, $($item.value.Length) chars)" } else { "(EMPTY)" }
    Write-Host ("    {0,-32} = {1}" -f $item.name, $val)
}
Write-Host ("  Total: {0} variable(s)" -f $sorted.Count) -ForegroundColor Cyan
Write-Host ""
Write-Host "Done. Trigger the manual sweep to validate." -ForegroundColor Green
