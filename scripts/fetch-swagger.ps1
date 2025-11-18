$defaultUrls = @(
    'https://localhost:7215/swagger/v1/swagger.json',
    'http://localhost:5022/swagger/v1/swagger.json'
)

# Allow override via env var TSIC_SWAGGER_URL (comma-separated list)
$envList = $env:TSIC_SWAGGER_URL
if ($envList) {
    $urls = $envList.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
} else {
    $urls = $defaultUrls
}

$max = 30
for ($i = 0; $i -lt $max; $i++) {
    foreach ($url in $urls) {
        try {
            # Primary: Invoke-RestMethod (ignore cert issues via -SkipCertificateCheck where available)
            try {
                $r = Invoke-RestMethod -Uri $url -UseBasicParsing -ErrorAction Stop
                $r | ConvertTo-Json -Depth 10
                exit 0
            } catch {
                # Fallback: curl.exe (-k to ignore cert validation on https)
                $out = & curl.exe -sS -k $url 2>$null
                if ($LASTEXITCODE -eq 0 -and $out) {
                    Write-Output $out
                    exit 0
                }
                throw
            }
        } catch {
            # Try next URL or wait a bit before next loop
        }
    }
    Start-Sleep -Seconds 1
}

Write-Error 'Failed to fetch swagger after timeout'
exit 2
