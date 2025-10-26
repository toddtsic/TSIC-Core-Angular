# Start TSIC Development Environment
# This script launches both the Angular dev server and the .NET API in the correct order

Write-Host "Starting TSIC Development Environment..." -ForegroundColor Cyan

# Change to the Angular project directory
$angularPath = Join-Path $PSScriptRoot "TSIC-Core-Angular"
Set-Location $angularPath

# Step 1: Start Angular Dev Server in a new window
Write-Host "`n[1/2] Starting Angular Dev Server..." -ForegroundColor Green
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$angularPath'; npm start"

# Wait a bit for Angular to start initializing
Write-Host "Waiting for Angular dev server to initialize..." -ForegroundColor Yellow
Start-Sleep -Seconds 8

# Step 2: Launch the debugger (API + Frontend)
Write-Host "`n[2/2] Launching API and attaching debugger..." -ForegroundColor Green
Write-Host "Opening VS Code debugger - select 'Launch API + Frontend (https)' configuration" -ForegroundColor Yellow

# Return to the workspace root
Set-Location $PSScriptRoot

# Open VS Code and trigger the debug command
# Note: The user will need to press F5 or click the debug button to start the API
Write-Host "`nIMPORTANT: Press F5 in VS Code and select 'Launch API + Frontend (https)' to start debugging" -ForegroundColor Cyan
Write-Host "`nAngular: https://localhost:4200" -ForegroundColor White
Write-Host "API: https://localhost:7149" -ForegroundColor White
Write-Host "`nThe Angular dev server is running in a separate PowerShell window." -ForegroundColor Gray
