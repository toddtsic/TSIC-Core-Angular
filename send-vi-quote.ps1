<#
Usage:
  # From repo root (this folder):
  #   powershell -ExecutionPolicy Bypass -File .\send-vi-quote.ps1
  # Or specify a different payload path:
  #   powershell -ExecutionPolicy Bypass -File .\send-vi-quote.ps1 -PayloadPath .\payload.json

Notes:
  - You can set an environment variable VI_AUTH to override the Authorization header value (Base64(clientId:secret)).
    Example (PowerShell):  $env:VI_AUTH = "<base64>"
  - Defaults to the known test value if VI_AUTH is not set.
#>

param(
  [string]$PayloadPath
)

if (-not $PayloadPath) {
  $PayloadPath = Join-Path $PSScriptRoot 'payload.json'
}

if (-not (Test-Path -LiteralPath $PayloadPath)) {
  Write-Error "payload.json not found at $PayloadPath"
  exit 1
}

$authValue = if ($env:VI_AUTH) { "Basic $($env:VI_AUTH)" } else { "Basic dGVzdF9HUkVWSEtGSEpZODdDR1dXOVJGMTVKRDUwVzVQUFE3VTp0ZXN0X0p0bEVFQmtGTk55YkdMeU93Q0NGVWVRcTlqM3pLOWRVRUpmSk1leXFQTVJqTXNXZnpVazBKUnFvSHlweG9mWkpxZUg1bnVLMDA0MllkNVRwWE1aT2Y4eVZqOVg5WURGaTdMVzUwQURzVlhEeXp1aWlxOUhMVm9wYndhTlh3cVdJ" }

$uri = "https://api.verticalinsure.com/v1/quote/registration-cancellation"

$curlArgs = @(
  '-sS', '-L', $uri,
  '-H', 'Content-Type: application/json',
  '-H', 'Accept: application/json',
  '-H', "Authorization: $authValue",
  '--data-binary', "@$PayloadPath"
)

# Invoke curl.exe with proper argument array to avoid quoting issues
& curl.exe @curlArgs