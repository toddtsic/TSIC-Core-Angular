# Simple workaround: Use .NET dev-certs HTTPS certificate for Angular
# Since we can't easily export the private key in PowerShell 5.1,
# we'll just disable SSL for Angular and use HTTP instead

Write-Host "Due to PowerShell limitations in exporting private keys," -ForegroundColor Yellow
Write-Host "the recommended approach is to use one of these options:" -ForegroundColor Yellow
Write-Host ""
Write-Host "Option 1: Install mkcert (Recommended)" -ForegroundColor Cyan
Write-Host "  1. Download mkcert from: https://github.com/FiloSottile/mkcert/releases" -ForegroundColor White
Write-Host "  2. Run: mkcert -install" -ForegroundColor White
Write-Host "  3. cd to: TSIC-Core-Angular\src\frontend\tsic-app\ssl" -ForegroundColor White
Write-Host "  4. Run: mkcert -key-file localhost.key -cert-file localhost.crt localhost" -ForegroundColor White
Write-Host ""
Write-Host "Option 2: Use HTTP for Angular (simpler for development)" -ForegroundColor Cyan
Write-Host "  - Angular: http://localhost:4200" -ForegroundColor White
Write-Host "  - API: https://localhost:7215 (already configured)" -ForegroundColor White
Write-Host "  - This works fine for development since they're both localhost" -ForegroundColor White
Write-Host ""
Write-Host "For now, keeping current HTTP configuration for Angular." -ForegroundColor Green
