# Run Angular frontend tests for Player Registration
# Uses Angular 21 built-in test runner (@angular/build:unit-test)
$ErrorActionPreference = 'Stop'
$frontendRoot = Join-Path $PSScriptRoot '..\..\..\TSIC-Core-Angular\src\frontend\tsic-app'
Push-Location $frontendRoot
try {
    Write-Host "Running Angular tests from: $frontendRoot" -ForegroundColor Cyan
    npx ng test --watch=false
    Write-Host "`nTests complete." -ForegroundColor Green
}
finally {
    Pop-Location
}
