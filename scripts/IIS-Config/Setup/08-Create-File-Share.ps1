# ============================================================================
# 08-Create-File-Share.ps1 — Create SMB file share for remote deployment
# ============================================================================
# Run this ON THE PROD SERVER (TSIC-PHOENIX) as Administrator.
# Creates an SMB share on the Websites folder so the dev machine can
# deploy directly via UNC path (\\TSIC-PHOENIX\Websites).
#
# Security:
#   - Share is restricted to a single user account (FullControl)
#   - NTFS permissions are the real gate (set by 03-Create-Directories.ps1)
#   - SMB port 445 should NOT be open to the internet (firewall blocks WAN)
#
# Usage:
#   .\08-Create-File-Share.ps1 -Environment Prod
#   .\08-Create-File-Share.ps1 -Environment Prod -AllowedUser "TSIC-PHOENIX\Administrator"
# ============================================================================

#Requires -RunAsAdministrator

param(
    [ValidateSet('Dev', 'Prod')]
    [string]$Environment = 'Prod',

    [string]$AllowedUser
)

. "$PSScriptRoot\..\_config.ps1" -Environment $Environment

Write-Host ""
Write-Host "[Step 8] Creating SMB file share ($Environment)..." -ForegroundColor Green

$shareName = 'Websites'
$sharePath = $Config.BasePath

# Resolve allowed user
if (-not $AllowedUser) {
    $AllowedUser = "$env:COMPUTERNAME\Administrator"
    Write-Host "  No -AllowedUser specified, defaulting to: $AllowedUser" -ForegroundColor Yellow
}

# Verify the folder exists
if (-not (Test-Path $sharePath)) {
    Write-Host "  ERROR: $sharePath does not exist. Run 03-Create-Directories.ps1 first." -ForegroundColor Red
    exit 1
}

# Create or update the share
$existingShare = Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue
if ($existingShare) {
    Write-Host "  Share '\\$env:COMPUTERNAME\$shareName' already exists." -ForegroundColor DarkGray
    Write-Host "  Path: $($existingShare.Path)" -ForegroundColor White

    # Verify it points to the right path
    if ($existingShare.Path -ne $sharePath) {
        Write-Host "  WARNING: Share points to $($existingShare.Path), expected $sharePath" -ForegroundColor Yellow
        Write-Host "  Removing and recreating..." -ForegroundColor Yellow
        Remove-SmbShare -Name $shareName -Force
        $existingShare = $null
    }
}

if (-not $existingShare) {
    New-SmbShare -Name $shareName `
        -Path $sharePath `
        -FullAccess $AllowedUser `
        -Description "TSIC IIS deployment share — restricted to $AllowedUser" | Out-Null
    Write-Host "  Created share: \\$env:COMPUTERNAME\$shareName" -ForegroundColor Green
    Write-Host "  Path:          $sharePath" -ForegroundColor White
    Write-Host "  Full access:   $AllowedUser" -ForegroundColor White
}

# Verify share permissions — should ONLY have the allowed user
Write-Host ""
Write-Host "  Share permissions:" -ForegroundColor Yellow
$perms = Get-SmbShareAccess -Name $shareName
$perms | ForEach-Object {
    $color = if ($_.AccountName -eq $AllowedUser) { 'Green' } else { 'Red' }
    Write-Host "    $($_.AccountName): $($_.AccessRight) ($($_.AccessControlType))" -ForegroundColor $color
}

# Remove 'Everyone' if present (Windows adds it by default on some versions)
$everyoneAccess = $perms | Where-Object { $_.AccountName -eq 'Everyone' }
if ($everyoneAccess) {
    Write-Host ""
    Write-Host "  Removing 'Everyone' from share permissions..." -ForegroundColor Yellow
    Revoke-SmbShareAccess -Name $shareName -AccountName 'Everyone' -Force | Out-Null
    Write-Host "  Removed." -ForegroundColor Green
}

# Verify connectivity hint
Write-Host ""
Write-Host "[Step 8] Complete." -ForegroundColor Green
Write-Host ""
Write-Host "  Test from dev machine:" -ForegroundColor Yellow
Write-Host "    dir \\$env:COMPUTERNAME\$shareName" -ForegroundColor White
Write-Host ""
Write-Host "  If access denied, ensure the dev machine authenticates as $AllowedUser:" -ForegroundColor Yellow
Write-Host "    net use \\$env:COMPUTERNAME\$shareName /user:$AllowedUser *" -ForegroundColor White
