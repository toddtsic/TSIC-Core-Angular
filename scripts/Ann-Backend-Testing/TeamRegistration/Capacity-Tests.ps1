<#
.SYNOPSIS
    Runs team registration capacity tests.

.DESCRIPTION
    Two test suites:
      1. AgegroupCapacityTests -- agegroup-level team limits (MaxTeams)
         and automatic WAITLIST agegroup creation
      2. MaxTeamsPerClubTests -- per-club team limits within an agegroup

    These tests ensure:
      - Full agegroups block or waitlist new teams
      - WAITLIST agegroups are created with correct naming
      - Clubs cannot exceed their per-agegroup team quota
      - Unlimited settings (0) skip capacity checks entirely

.EXAMPLE
    .\Capacity-Tests.ps1
#>

$ErrorActionPreference = "Stop"
$testProject = Join-Path $PSScriptRoot "..\..\..\TSIC-Core-Angular\src\backend\TSIC.Tests\TSIC.Tests.csproj"

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "    Team Registration Capacity Tests" -ForegroundColor Cyan
Write-Host "    (agegroup limits + per-club limits)" -ForegroundColor Gray
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

# Build first to catch compile errors separately
Write-Host "  Building test project..." -ForegroundColor Gray
$buildOutput = dotnet build $testProject --no-restore --verbosity quiet 2>&1
$buildFailed = $LASTEXITCODE -ne 0

if ($buildFailed) {
    Write-Host "  BUILD FAILED" -ForegroundColor Red
    Write-Host ""
    foreach ($line in $buildOutput) {
        if ($line -match "error CS") {
            Write-Host "    $line" -ForegroundColor Red
        }
    }
    Write-Host ""
    exit 1
}
Write-Host "  Build OK" -ForegroundColor Green
Write-Host ""

# Run tests filtered to both capacity test suites
$output = dotnet test $testProject --filter "FullyQualifiedName~AgegroupCapacity|FullyQualifiedName~MaxTeamsPerClub" --no-build --no-restore --verbosity normal 2>&1
$passed = @(); $failed = @()

foreach ($line in $output) {
    if ($line -match "^\s*Passed\s+(.+)\s+\[") { $passed += $Matches[1].Trim() }
    elseif ($line -match "^\s*Failed\s+(.+)\s+\[") { $failed += $Matches[1].Trim() }
}

# ── Results ──────────────────────────────────────────────────────

if ($passed.Count -gt 0) {
    Write-Host "  PASSED:" -ForegroundColor Green
    foreach ($t in $passed) { Write-Host "    [PASS] $t" -ForegroundColor Green }
}

if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "  FAILED:" -ForegroundColor Red
    foreach ($t in $failed) { Write-Host "    [FAIL] $t" -ForegroundColor Red }

    # Extract error messages for failed tests
    Write-Host ""
    Write-Host "  Error details:" -ForegroundColor Yellow
    $inError = $false
    foreach ($line in $output) {
        if ($line -match "Error Message:") { $inError = $true }
        if ($inError -and $line -match "Stack Trace:") { $inError = $false }
        if ($inError) { Write-Host "    $line" -ForegroundColor Yellow }
    }
}

$total = $passed.Count + $failed.Count
Write-Host ""
Write-Host "  ----------------------------------------" -ForegroundColor Gray

if ($total -eq 0) {
    Write-Host "  No capacity tests found!" -ForegroundColor Yellow
    Write-Host "  Check namespaces: TSIC.Tests.TeamRegistration.AgegroupCapacity / MaxTeamsPerClub" -ForegroundColor Yellow
} elseif ($failed.Count -eq 0) {
    Write-Host "  ALL $total TESTS PASSED" -ForegroundColor Green
} else {
    Write-Host "  $($passed.Count) passed / $($failed.Count) failed / $total total" -ForegroundColor Red
}

Write-Host ""
