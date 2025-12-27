<#
.SYNOPSIS
    Regenerates TypeScript API models from Swagger/OpenAPI specification
.DESCRIPTION
    This script:
    1. Checks if the API is running (starts it if needed)
    2. Waits for Swagger endpoint to be available
    3. Generates TypeScript models using openapi-typescript-codegen
    4. Verifies the generated files
.EXAMPLE
    .\2-Regenerate-API-Models.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  TypeScript API Model Regeneration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Navigate to project root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

# Use forward slashes for cross-platform compatibility, PowerShell will normalize
$apiPath = Join-Path $projectRoot "TSIC-Core-Angular/src/backend/TSIC.API" -Resolve
$frontendPath = Join-Path $projectRoot "TSIC-Core-Angular/src/frontend/tsic-app" -Resolve

Write-Host "Project root: $projectRoot" -ForegroundColor Gray
Write-Host "API path: $apiPath" -ForegroundColor Gray
Write-Host "Frontend path: $frontendPath" -ForegroundColor Gray
Write-Host ""

# Check if API is running
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Step 1: Verify API Status" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$apiRunning = $false
$swaggerUrl = "http://localhost:5022/swagger/v1/swagger.json"
$swaggerUrlHttps = "https://localhost:7215/swagger/v1/swagger.json"

Write-Host "Checking for API at $swaggerUrl..." -ForegroundColor Yellow

try {
    $response = Invoke-WebRequest -Uri $swaggerUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
    $apiRunning = $response.StatusCode -eq 200
    if ($apiRunning) {
        Write-Host "[OK] API is running on HTTP (port 5022)" -ForegroundColor Green
    }
} catch {
    Write-Host "  HTTP endpoint not responding, trying HTTPS..." -ForegroundColor Gray
    try {
        $response = Invoke-WebRequest -Uri $swaggerUrlHttps -UseBasicParsing -SkipCertificateCheck -TimeoutSec 2 -ErrorAction SilentlyContinue
        $apiRunning = $response.StatusCode -eq 200
        if ($apiRunning) {
            Write-Host "[OK] API is running on HTTPS (port 7215)" -ForegroundColor Green
        }
    } catch {
        Write-Host "[X] API is not running" -ForegroundColor Red
    }
}

if (-not $apiRunning) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Starting API Server" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Starting API in new window..." -ForegroundColor Yellow
    Write-Host "API Path: $apiPath" -ForegroundColor Gray
    
    Set-Location $apiPath
    
    # Start API in a new PowerShell window
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = "powershell.exe"
    $startInfo.Arguments = "-NoExit -Command `"dotnet watch run --launch-profile https --non-interactive`""
    $startInfo.WorkingDirectory = $apiPath
    $startInfo.UseShellExecute = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    
    Set-Location $projectRoot
    
    Write-Host "API process started (PID: $($process.Id))" -ForegroundColor Green
    Write-Host ""
    Write-Host "Waiting for API to be ready (checking every 2 seconds)..." -ForegroundColor Yellow
    
    $maxAttempts = 30
    $attempt = 0
    
    while ($attempt -lt $maxAttempts -and -not $apiRunning) {
        Start-Sleep -Seconds 2
        $attempt++
        
        Write-Host "." -NoNewline
        
        try {
            $response = Invoke-WebRequest -Uri $swaggerUrl -UseBasicParsing -TimeoutSec 1 -ErrorAction SilentlyContinue
            $apiRunning = $response.StatusCode -eq 200
        } catch {
            try {
                $response = Invoke-WebRequest -Uri $swaggerUrlHttps -UseBasicParsing -SkipCertificateCheck -TimeoutSec 1 -ErrorAction SilentlyContinue
                $apiRunning = $response.StatusCode -eq 200
            } catch {
                # Still not ready
            }
        }
    }
    
    Write-Host ""
    
    if (-not $apiRunning) {
        Write-Host ""
        Write-Host "ERROR: API failed to start within 60 seconds" -ForegroundColor Red
        Write-Host "Please check the API window for errors" -ForegroundColor Yellow
        exit 1
    }
    
    Write-Host ""
    Write-Host "[OK] API is now ready" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Step 2: Generate TypeScript Models" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Set-Location $frontendPath

try {
    $generateCommand = "npm run generate:api"
    Write-Host "Running: $generateCommand" -ForegroundColor Gray
    Write-Host "Working directory: $frontendPath" -ForegroundColor Gray
    
    $output = cmd /c "npm run generate:api" 2>&1
    Write-Host $output
    
    if ($LASTEXITCODE -ne 0) {
        throw "npm run generate:api failed with exit code $LASTEXITCODE"
    }
    
    Write-Host "Model generation completed" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Step 3: Verify Generated Models" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Verify generated files
    Write-Host "Verifying generated models..." -ForegroundColor Yellow
    
    $indexPath = Join-Path $frontendPath "src\app\core\api\models\index.ts"
    $familyPlayerPath = Join-Path $frontendPath "src\app\core\api\models\models\FamilyPlayerDto.ts"
    $jobMetadataPath = Join-Path $frontendPath "src\app\core\api\models\models\JobMetadataResponse.ts"
    
    if (-not (Test-Path $indexPath)) {
        throw "Barrel export (index.ts) not found"
    }
    
    if (-not (Test-Path $familyPlayerPath)) {
        throw "FamilyPlayerDto.ts not found"
    }
    
    if (-not (Test-Path $jobMetadataPath)) {
        throw "JobMetadataResponse.ts not found"
    }
    
    # Verify content
    $familyPlayerContent = Get-Content $familyPlayerPath -Raw
    if ($familyPlayerContent -notmatch "playerId") {
        throw "FamilyPlayerDto missing playerId field"
    }
    
    $jobMetadataContent = Get-Content $jobMetadataPath -Raw
    if ($jobMetadataContent -notmatch "jobBannerText1") {
        throw "JobMetadataResponse missing jobBannerText1 field"
    }
    
    Write-Host "[OK] Barrel export verified" -ForegroundColor Green
    Write-Host "[OK] FamilyPlayerDto verified" -ForegroundColor Green
    Write-Host "[OK] JobMetadataResponse verified" -ForegroundColor Green
    Write-Host "[OK] Required fields present" -ForegroundColor Green
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Complete - All models generated successfully!" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    Set-Location $projectRoot
    exit 1
} finally {
    Set-Location $projectRoot
}
