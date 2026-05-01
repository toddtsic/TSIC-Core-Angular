# =============================================================================
# Apply-Prod-Gap-Fixes.ps1
#
# One-shot remediation for two configuration gaps identified by
# Verify-Prod-Readiness.ps1 on TSIC-PHOENIX:
#
#   1. Application pool 'claude-api' missing LoadUserProfile=true
#      Symptom: ASP.NET Core uses ephemeral DataProtection keys; users get
#      logged out / 400 Bad Request on POST after every IIS recycle.
#
#   2. App pool 'claude-api' missing Modify ACL on user-upload top-level
#      folders under E:\Websites\TSIC-STATICS (BannerFiles, RegFileUploads).
#      Inherited ACL covers nested subfolders (RegFileUploads\MedForms,
#      RegFileUploads\VaccineCards, etc.).
#      Symptom: 500 Internal Server Error on first upload.
#
# Both fixes are idempotent. Re-running is safe.
# Paste-safe (no param block, no exit calls).
#
# Run as Administrator on TSIC-PHOENIX.
# =============================================================================

# --- Edit only if reality differs -------------------------------------------
$FixAppPoolName  = 'claude-api'
$FixStaticsBase  = 'E:\Websites\TSIC-STATICS'
$FixStaticsDirs  = @('BannerFiles', 'RegFileUploads')   # subfolders inherit via (OI)(CI) /T
# ----------------------------------------------------------------------------

$ErrorActionPreference = 'Continue'
$FixPoolPrincipal = "IIS APPPOOL\$FixAppPoolName"

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Apply Prod Gap Fixes' -ForegroundColor Cyan
Write-Host "  Pool         : $FixPoolPrincipal"
Write-Host "  Statics base : $FixStaticsBase"
Write-Host "  Subfolders   : $($FixStaticsDirs -join ', ')"
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

# --- Fix #1: LoadUserProfile = true ----------------------------------------
Write-Host 'Fix #1: LoadUserProfile = true' -ForegroundColor White
try {
    Import-Module WebAdministration -DisableNameChecking -ErrorAction Stop | Out-Null

    $poolPath = "IIS:\AppPools\$FixAppPoolName"
    if (-not (Test-Path $poolPath)) {
        Write-Host "  [FAIL] App pool '$FixAppPoolName' does not exist." -ForegroundColor Red
    }
    else {
        $current = (Get-Item $poolPath).processModel.loadUserProfile
        if ($current) {
            Write-Host "  [SKIP] LoadUserProfile already true." -ForegroundColor DarkGray
        }
        else {
            Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true
            Write-Host "  [DONE] Set LoadUserProfile = true." -ForegroundColor Green
        }

        Write-Host "  [INFO] Recycling pool to apply..." -ForegroundColor White
        Restart-WebAppPool -Name $FixAppPoolName
        Write-Host "  [DONE] Pool recycled." -ForegroundColor Green
    }
}
catch {
    Write-Host "  [FAIL] $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ''

# --- Fix #2: Statics subfolder Modify ACLs ---------------------------------
Write-Host 'Fix #2: Statics subfolder Modify ACLs' -ForegroundColor White

if (-not (Test-Path $FixStaticsBase)) {
    Write-Host "  [FAIL] Statics base does not exist: $FixStaticsBase" -ForegroundColor Red
}
else {
    foreach ($sub in $FixStaticsDirs) {
        $dir = Join-Path $FixStaticsBase $sub
        try {
            if (-not (Test-Path $dir)) {
                New-Item -ItemType Directory -Path $dir -Force | Out-Null
                Write-Host "  [DONE] Created $dir." -ForegroundColor Green
            }

            $acl = Get-Acl $dir
            $existing = $acl.Access | Where-Object {
                $_.IdentityReference.Value -ieq $FixPoolPrincipal -and
                ($_.FileSystemRights -match 'Modify|FullControl')
            }
            if ($existing) {
                Write-Host "  [SKIP] $sub : $FixPoolPrincipal already has $($existing.FileSystemRights)." -ForegroundColor DarkGray
            }
            else {
                # icacls handles IIS APPPOOL virtual accounts more reliably than .NET ACL APIs
                $output = & icacls $dir /grant "${FixPoolPrincipal}:(OI)(CI)M" /T 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "  [DONE] $sub : Granted Modify (inherited)." -ForegroundColor Green
                }
                else {
                    Write-Host "  [FAIL] $sub : icacls returned $LASTEXITCODE :" -ForegroundColor Red
                    $output | ForEach-Object { Write-Host "         $_" -ForegroundColor DarkGray }
                }
            }
        }
        catch {
            Write-Host "  [FAIL] $sub : $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' Done. Re-run Verify-Prod-Readiness.ps1 to confirm both' -ForegroundColor Cyan
Write-Host ' previously-failing checks now PASS.' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''
