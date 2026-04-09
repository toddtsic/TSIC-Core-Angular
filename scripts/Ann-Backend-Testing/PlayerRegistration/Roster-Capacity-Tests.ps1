<#
.SYNOPSIS
    Runs player roster capacity tests.

.DESCRIPTION
    Validates that player registration enforces team roster limits:
      - Team with room accepts registration
      - Full team blocks registration (IsFull = true)
      - Full team with waitlist redirects to waitlist team
      - MaxCount = 0 means unlimited

    These tests ensure parents are told immediately at team selection
    when a team is full, not after filling out 3 more wizard steps.

.EXAMPLE
    .\Roster-Capacity-Tests.ps1
#>

$ErrorActionPreference = "Stop"
$testProject = Join-Path $PSScriptRoot "..\..\..\TSIC-Core-Angular\src\backend\TSIC.Tests\TSIC.Tests.csproj"

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "    Player Roster Capacity Tests" -ForegroundColor Cyan
Write-Host "    (player registration -- team limits)" -ForegroundColor Gray
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

# Run tests filtered to roster capacity
$output = dotnet test $testProject --filter "FullyQualifiedName~RosterCapacity" --no-build --no-restore --verbosity normal 2>&1
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
    Write-Host "  No roster capacity tests found!" -ForegroundColor Yellow
    Write-Host "  Check that test classes are in namespace TSIC.Tests.PlayerRegistration.RosterCapacity" -ForegroundColor Yellow
} elseif ($failed.Count -eq 0) {
    Write-Host "  ALL $total TESTS PASSED" -ForegroundColor Green
} else {
    Write-Host "  $($passed.Count) passed / $($failed.Count) failed / $total total" -ForegroundColor Red
}

Write-Host ""
