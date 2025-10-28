# Setup SSL certificates for Angular development using .NET dev-certs

$ErrorActionPreference = "Stop"

Write-Host "Setting up SSL certificates for Angular development..." -ForegroundColor Cyan

# Define paths
$sslDir = Join-Path $PSScriptRoot "..\TSIC-Core-Angular\src\frontend\tsic-app\ssl"
$pfxPath = Join-Path $sslDir "localhost.pfx"
$certPath = Join-Path $sslDir "localhost.crt"
$keyPath = Join-Path $sslDir "localhost.key"
$password = "angular"

# Ensure SSL directory exists
if (-not (Test-Path $sslDir)) {
    New-Item -ItemType Directory -Path $sslDir -Force | Out-Null
}

Write-Host "Cleaning existing .NET dev certificates..." -ForegroundColor Yellow
dotnet dev-certs https --clean

Write-Host "Creating new .NET dev certificate..." -ForegroundColor Yellow
dotnet dev-certs https --trust

Write-Host "Exporting certificate to PFX..." -ForegroundColor Yellow
dotnet dev-certs https --export-path $pfxPath --password $password --format Pfx

Write-Host "Converting PFX to PEM format for Angular..." -ForegroundColor Yellow

# Use .NET to convert PFX to PEM (cert + key)
$pfxCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxPath, $password, [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)

# Export certificate (public key)
$certPem = "-----BEGIN CERTIFICATE-----`r`n"
$certPem += [Convert]::ToBase64String($pfxCert.RawData, [System.Base64FormattingOptions]::InsertLineBreaks)
$certPem += "`r`n-----END CERTIFICATE-----`r`n"
[System.IO.File]::WriteAllText($certPath, $certPem)

# Export private key
try {
    $rsaKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($pfxCert)
    $keyBytes = $rsaKey.ExportPkcs8PrivateKey()
    $keyPem = "-----BEGIN PRIVATE KEY-----`r`n"
    $keyPem += [Convert]::ToBase64String($keyBytes, [System.Base64FormattingOptions]::InsertLineBreaks)
    $keyPem += "`r`n-----END PRIVATE KEY-----`r`n"
    [System.IO.File]::WriteAllText($keyPath, $keyPem)
    
    Write-Host "`nSSL certificates created successfully!" -ForegroundColor Green
    Write-Host "Location: $sslDir" -ForegroundColor Green
    Write-Host "`nFiles created:" -ForegroundColor Cyan
    Write-Host "  - localhost.pfx (password: $password)" -ForegroundColor White
    Write-Host "  - localhost.crt (certificate)" -ForegroundColor White
    Write-Host "  - localhost.key (private key)" -ForegroundColor White
    Write-Host "`nNext steps:" -ForegroundColor Cyan
    Write-Host "  1. Restart your Angular dev server" -ForegroundColor White
    Write-Host "  2. Navigate to https://localhost:4200" -ForegroundColor White
    Write-Host "  3. Accept the certificate if prompted" -ForegroundColor White
}
catch {
    Write-Host "`nError: Could not export private key." -ForegroundColor Red
    Write-Host "This PowerShell version may not support the required crypto operations." -ForegroundColor Yellow
    Write-Host "`nAlternative: Install mkcert for easier certificate management:" -ForegroundColor Yellow
    Write-Host "  choco install mkcert" -ForegroundColor White
    Write-Host "  mkcert -install" -ForegroundColor White
    Write-Host "  cd $sslDir" -ForegroundColor White
    Write-Host "  mkcert localhost" -ForegroundColor White
    exit 1
}
