# Build and Publish .NET API Script
# Run this from the solution root directory

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "$PSScriptRoot\..\publish\api",
    [string]$ProjectPath = "$PSScriptRoot\..\TSIC-Core-Angular\src\backend\TSIC.API\TSIC.API.csproj",
    [string]$Runtime = "" # e.g., "win-x64" to include only x64 native assets
)

Write-Host "Building TSIC .NET API..." -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Output Path: $OutputPath" -ForegroundColor Yellow

# Ensure output directory exists
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Clean output directory before building
Write-Host "Cleaning old build output..." -ForegroundColor Cyan
Get-ChildItem -Path $OutputPath -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

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

# Publish API with all dependencies (publish rebuilds to ensure all deps are included)
Write-Host "Publishing API..." -ForegroundColor Cyan
$publishArgs = @($ProjectPath, '--configuration', $Configuration, '--output', $OutputPath)
if ($Runtime -and $Runtime.Trim().Length -gt 0) {
    Write-Host "Target Runtime: $Runtime (framework-dependent)" -ForegroundColor Yellow
    # Specify RID to include only that platform's native assets; remain framework-dependent
    $publishArgs += @('-r', $Runtime, '--self-contained', 'false')
}
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
    exit 1
}

Write-Host "API build and publish completed successfully!" -ForegroundColor Green
Write-Host "Output location: $OutputPath" -ForegroundColor Green

# List published files
Write-Host "Published files:" -ForegroundColor Cyan
Get-ChildItem $OutputPath -Name | ForEach-Object { Write-Host "  $_" }

# Ensure web.config template is used and well-formed
try {
    $scriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $apiConfigSrc = Join-Path $scriptsDir 'web.config.api'
    if (Test-Path $apiConfigSrc) {
        Copy-Item $apiConfigSrc (Join-Path $OutputPath 'web.config') -Force
        $path = Join-Path $OutputPath 'web.config'
        $raw = Get-Content $path -Raw
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success -and $m.Value.Length -ne $raw.Length) {
            Set-Content -Path $path -Value $m.Value -Encoding UTF8
        }
        [void]([xml](Get-Content $path -Raw))
    }
} catch {
    Write-Warning ("API web.config validation note: {0}" -f $_.Exception.Message)
}

# Backup published output to zip (only if build was successful)
if (Test-Path $OutputPath) {
    $fileCount = (Get-ChildItem $OutputPath -Recurse -File).Count
    if ($fileCount -gt 0) {
        Write-Host "Creating backup archive..." -ForegroundColor Cyan
        $backupDir = "C:\Users\Administrator\Documents\Backups\DOTNETPublishedOutputs"
        $zipPath = Join-Path $backupDir "TSIC.api.zip"

        # Ensure backup directory exists
        if (!(Test-Path $backupDir)) {
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        }

        # Remove old zip if it exists
        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }

        # Create temp staging folder with project name
        $tempStaging = Join-Path $env:TEMP "TSIC-Api-Staging-$(Get-Date -Format 'yyyyMMddHHmmss')"
        $projectFolder = Join-Path $tempStaging "TSIC.Api"
        New-Item -ItemType Directory -Path $projectFolder -Force | Out-Null
        Copy-Item -Path "$OutputPath\*" -Destination $projectFolder -Recurse -Force

        # Create zip archive from staging folder
        Compress-Archive -Path "$tempStaging\*" -DestinationPath $zipPath -CompressionLevel Optimal
        
        # Clean up staging folder
        Remove-Item $tempStaging -Recurse -Force -ErrorAction SilentlyContinue
        if ($?) {
            Write-Host "Backup archive created: $zipPath" -ForegroundColor Green
        } else {
            Write-Warning "Failed to create backup archive"
        }
    } else {
        Write-Warning "No files found in output directory, skipping backup archive"
    }
} else {
    Write-Warning "Output path does not exist, skipping backup archive"
}