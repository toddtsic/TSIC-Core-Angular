# ============================================================================
# 01-Build-DotNet-API.ps1 — Build and publish .NET API
# ============================================================================
# Restores, builds, publishes to publish/api/, copies web.config template,
# and creates a backup zip.
# ============================================================================

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "$PSScriptRoot\..\..\..\publish\api",
    [string]$ProjectPath = "$PSScriptRoot\..\..\..\TSIC-Core-Angular\src\backend\TSIC.API\TSIC.API.csproj",
    [string]$Runtime = ""
)

Write-Host "Building TSIC .NET API..." -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Output Path: $OutputPath" -ForegroundColor Yellow

# Ensure output directory exists
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Clean output directory
Write-Host "Cleaning old build output..." -ForegroundColor Cyan
Get-ChildItem -Path $OutputPath -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

# Navigate to solution directory
$SolutionDir = "$PSScriptRoot\..\..\..\TSIC-Core-Angular"
Push-Location $SolutionDir

try {
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

    # Publish API
    Write-Host "Publishing API..." -ForegroundColor Cyan
    $publishArgs = @($ProjectPath, '--configuration', $Configuration, '--output', $OutputPath)
    if ($Runtime -and $Runtime.Trim().Length -gt 0) {
        Write-Host "Target Runtime: $Runtime (framework-dependent)" -ForegroundColor Yellow
        $publishArgs += @('-r', $Runtime, '--self-contained', 'false')
    }
    dotnet publish @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed!"
        exit 1
    }
}
finally {
    Pop-Location
}

Write-Host "API published successfully!" -ForegroundColor Green
Write-Host "Output location: $OutputPath" -ForegroundColor Green

# Copy web.config template
$webConfigSrc = Join-Path $PSScriptRoot "..\web.config.api"
if (Test-Path $webConfigSrc) {
    Copy-Item $webConfigSrc (Join-Path $OutputPath 'web.config') -Force
    try {
        $path = Join-Path $OutputPath 'web.config'
        $raw = Get-Content $path -Raw
        $m = [regex]::Match($raw, '.*</configuration>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if ($m.Success -and $m.Value.Length -ne $raw.Length) {
            Set-Content -Path $path -Value $m.Value -Encoding UTF8
        }
        [void]([xml](Get-Content $path -Raw))
        Write-Host "  web.config.api applied and validated." -ForegroundColor Green
    } catch {
        Write-Warning "API web.config validation note: $($_.Exception.Message)"
    }
}

# Backup to zip
if (Test-Path $OutputPath) {
    $fileCount = (Get-ChildItem $OutputPath -Recurse -File).Count
    if ($fileCount -gt 0) {
        Write-Host "Creating backup archive..." -ForegroundColor Cyan
        $backupDir = Join-Path $PSScriptRoot "..\..\..\publish\build-archives"
        $zipPath = Join-Path $backupDir "TSIC.api.zip"
        if (!(Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

        $tempStaging = Join-Path $env:TEMP "TSIC-Api-Staging-$(Get-Date -Format 'yyyyMMddHHmmss')"
        $projectFolder = Join-Path $tempStaging "claude-api"
        New-Item -ItemType Directory -Path $projectFolder -Force | Out-Null
        Copy-Item -Path "$OutputPath\*" -Destination $projectFolder -Recurse -Force
        Compress-Archive -Path "$tempStaging\*" -DestinationPath $zipPath -CompressionLevel Optimal
        Remove-Item $tempStaging -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Backup archive created: $zipPath" -ForegroundColor Green
    }
}
