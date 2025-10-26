$url = 'https://localhost:7215/swagger/v1/swagger.json'
$max = 30

for ($i = 0; $i -lt $max; $i++) {
    try {
        # Try to fetch Swagger JSON. Use curl.exe as fallback if Invoke-RestMethod fails (certs, PS versions).
        try {
            $r = Invoke-RestMethod -Uri $url -UseBasicParsing -ErrorAction Stop
            $r | ConvertTo-Json -Depth 10
            exit 0
        } catch {
            # fallback to curl.exe (ignore cert validation with -k)
            $out = & curl.exe -sS -k $url 2>$null
            if ($LASTEXITCODE -eq 0 -and $out) {
                Write-Output $out
                exit 0
            }
            throw
        }
    } catch {
        Start-Sleep -Seconds 1
    }
}

Write-Error 'Failed to fetch swagger after timeout'
exit 2
