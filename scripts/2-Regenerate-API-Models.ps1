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
Write-Host "Checking API status..." -ForegroundColor Yellow

$apiRunning = $false
$swaggerUrl = "http://localhost:5022/swagger/v1/swagger.json"
$swaggerUrlHttps = "https://localhost:7215/swagger/v1/swagger.json"

try {
    $response = Invoke-WebRequest -Uri $swaggerUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
    $apiRunning = $response.StatusCode -eq 200
} catch {
    try {
        $response = Invoke-WebRequest -Uri $swaggerUrlHttps -UseBasicParsing -SkipCertificateCheck -TimeoutSec 2 -ErrorAction SilentlyContinue
        $apiRunning = $response.StatusCode -eq 200
    } catch {
        # API not running
    }
}

if (-not $apiRunning) {
    Write-Host "API not running. Starting API..." -ForegroundColor Yellow
    
    Set-Location $apiPath
    Start-Process pwsh -ArgumentList "-NoExit", "-Command", "dotnet watch run --launch-profile https --non-interactive"
    Set-Location $projectRoot
    
    Write-Host "Waiting for API to be ready..." -ForegroundColor Yellow
    
    $maxAttempts = 30
    $attempt = 0
    
    while ($attempt -lt $maxAttempts -and -not $apiRunning) {
        Start-Sleep -Seconds 2
        $attempt++
        
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
        
        Write-Host "." -NoNewline
    }
    
    Write-Host ""
    
    if (-not $apiRunning) {
        Write-Host "ERROR: API failed to start within 60 seconds" -ForegroundColor Red
        exit 1
    }
}

Write-Host "API is ready" -ForegroundColor Green
Write-Host ""

# Generate TypeScript models
Write-Host "Generating TypeScript models with openapi-typescript-codegen..." -ForegroundColor Yellow

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
    
    Write-Host "Generated models verified" -ForegroundColor Green
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Complete" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    Set-Location $projectRoot
    exit 1
} finally {
    Set-Location $projectRoot
}
