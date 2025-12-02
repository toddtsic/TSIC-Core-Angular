# Simple test script
param([switch]$KeepApiRunning)

Write-Host "Testing..." -ForegroundColor Cyan

try {
    Write-Host "In try block"
}
catch {
    Write-Host "In catch block"
}

Write-Host "Done!"
