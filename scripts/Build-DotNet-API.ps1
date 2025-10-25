# Build and Publish .NET API Script
# Run this from the solution root directory

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "$PSScriptRoot\..\publish\api",
    [string]$ProjectPath = "$PSScriptRoot\..\TSIC-Core-Angular\src\backend\TSIC.API\TSIC.API.csproj"
)

Write-Host "Building TSIC .NET API..." -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Output Path: $OutputPath" -ForegroundColor Yellow

# Ensure output directory exists
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Navigate to solution directory
$SolutionDir = "$PSScriptRoot\..\TSIC-Core-Angular"
cd $SolutionDir

# Restore packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "NuGet restore failed!"
    exit 1
}

# Build solution
Write-Host "Building solution..." -ForegroundColor Cyan
dotnet build --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# Run tests (optional, uncomment if you want to run tests before publish)
# Write-Host "Running tests..." -ForegroundColor Cyan
# dotnet test --configuration $Configuration --no-build --verbosity minimal
# if ($LASTEXITCODE -ne 0) {
#     Write-Error "Tests failed!"
#     exit 1
# }

# Publish API
Write-Host "Publishing API..." -ForegroundColor Cyan
dotnet publish $ProjectPath --configuration $Configuration --output $OutputPath --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
    exit 1
}

Write-Host "API build and publish completed successfully!" -ForegroundColor Green
Write-Host "Output location: $OutputPath" -ForegroundColor Green

# List published files
Write-Host "Published files:" -ForegroundColor Cyan
Get-ChildItem $OutputPath -Name | ForEach-Object { Write-Host "  $_" }