# ============================================================================
# 03-Create-Directories.ps1 — Create website directories with permissions
# ============================================================================
# Creates API, Angular, and Statics directories.
# Grants IIS_IUSRS read/execute on site roots.
# Grants app pool identity write access to logs/, keys/, and statics.
# ============================================================================

#Requires -RunAsAdministrator

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Prod'
)

. "$PSScriptRoot\..\_config.ps1" -Environment $Environment

Write-Host ""
Write-Host "[Step 3] Creating directories ($Environment)..." -ForegroundColor Green

# Create main directories
foreach ($dir in @($Config.ApiPath, $Config.AngularPath)) {
    if (Test-Path $dir) {
        Write-Host "  Directory exists: $dir" -ForegroundColor DarkGray
    }
    else {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Created: $dir" -ForegroundColor Green
    }
}

# Create API subdirectories that persist across deployments
foreach ($subdir in @('logs', 'keys')) {
    $subdirPath = Join-Path $Config.ApiPath $subdir
    if (-not (Test-Path $subdirPath)) {
        New-Item -ItemType Directory -Path $subdirPath -Force | Out-Null
        Write-Host "  Created: $subdirPath" -ForegroundColor Green
    }
}

# Set permissions: IIS_IUSRS read/execute on site roots
foreach ($dir in @($Config.ApiPath, $Config.AngularPath)) {
    $acl = Get-Acl $dir
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "IIS_IUSRS", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $dir -AclObject $acl
    Write-Host "  Granted IIS_IUSRS ReadAndExecute on $dir" -ForegroundColor White
}

# Grant app pool identity write access to logs/ and keys/
foreach ($subdir in @('logs', 'keys')) {
    $subdirPath = Join-Path $Config.ApiPath $subdir
    $acl = Get-Acl $subdirPath
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "IIS AppPool\$($Config.ApiPoolName)", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $subdirPath -AclObject $acl
    Write-Host "  Granted '$($Config.ApiPoolName)' Modify on $subdirPath" -ForegroundColor White
}

# Statics subfolders the API pool writes user-uploaded files into.
# Must exist and grant the API pool Modify before any upload endpoint will work.
# Inheritance flags propagate Modify to nested subfolders (e.g. RegFileUploads\MedForms,
# RegFileUploads\VaccineCards) so we only need to grant at the top level.
if (-not (Test-Path $Config.StaticsPath)) {
    New-Item -ItemType Directory -Path $Config.StaticsPath -Force | Out-Null
    Write-Host "  Created: $($Config.StaticsPath)" -ForegroundColor Green
}

foreach ($subdir in @('BannerFiles', 'RegFileUploads')) {
    $subdirPath = Join-Path $Config.StaticsPath $subdir
    if (-not (Test-Path $subdirPath)) {
        New-Item -ItemType Directory -Path $subdirPath -Force | Out-Null
        Write-Host "  Created: $subdirPath" -ForegroundColor Green
    }
    $acl = Get-Acl $subdirPath
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "IIS AppPool\$($Config.ApiPoolName)", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $subdirPath -AclObject $acl
    Write-Host "  Granted '$($Config.ApiPoolName)' Modify on $subdirPath" -ForegroundColor White
}

Write-Host "[Step 3] Complete." -ForegroundColor Green
