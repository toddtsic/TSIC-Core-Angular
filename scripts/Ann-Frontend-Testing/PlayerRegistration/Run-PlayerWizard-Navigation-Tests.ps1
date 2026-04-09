<#
.SYNOPSIS
    Runs the player wizard navigation / capacity notification tests.

.DESCRIPTION
    Validates what the registrant sees when clicking Continue:
      - Team full → warning toast, stays on Teams step
      - Waitlisted → warning toast with waitlist name, advances
      - Room available → advances silently
      - HTTP error → danger toast, stays on step
      - Review success/failure → advance or stay

    Uses Vitest (Angular's test runner) -- no browser window needed.

.EXAMPLE
    .\Run-PlayerWizard-Navigation-Tests.ps1
#>

$ErrorActionPreference = "Stop"
$frontendDir = Join-Path $PSScriptRoot "..\..\..\TSIC-Core-Angular\src\frontend\tsic-app"

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "    Player Wizard Navigation Tests" -ForegroundColor Cyan
Write-Host "    (capacity / waitlist notifications)" -ForegroundColor Gray
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

$testPattern = "src/app/views/registration/player/player.component.spec.ts"

Write-Host "  Running tests..." -ForegroundColor Gray
Write-Host ""

Push-Location $frontendDir
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    $rawOutput = & npx ng test --include="$testPattern" --watch=false --reporters=verbose 2>&1 | ForEach-Object { $_.ToString() }
} finally {
    $ErrorActionPreference = $prevEAP
    Pop-Location
}

# Strip ANSI escape codes for clean regex matching
$clean = $rawOutput | ForEach-Object { $_ -replace '\x1b\[[0-9;]*m', '' }

# Parse individual test results from verbose output
$passed = @(); $failed = @()
foreach ($line in $clean) {
    if ($line -match 'tsic-app\s+\S+\.spec\.ts\s+>\s+(.+?)\s+\d+ms') {
        $testName = $Matches[1].Trim()
        if ($line -match 'FAIL|×') {
            $failed += $testName
        } else {
            $passed += $testName
        }
    }
}

# Display results
if ($passed.Count -gt 0) {
    Write-Host "  PASSED:" -ForegroundColor Green
    foreach ($t in $passed) { Write-Host "    [PASS] $t" -ForegroundColor Green }
}
if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "  FAILED:" -ForegroundColor Red
    foreach ($t in $failed) { Write-Host "    [FAIL] $t" -ForegroundColor Red }
    Write-Host ""
    Write-Host "  --- Error Details ---" -ForegroundColor Yellow
    $inError = $false
    foreach ($line in $clean) {
        if ($line -match "AssertionError|AssertError|expected .+ to|received") { $inError = $true }
        if ($inError -and $line.Trim() -eq "") { $inError = $false }
        if ($inError) { Write-Host "    $line" -ForegroundColor Yellow }
    }
}

$total = $passed.Count + $failed.Count
Write-Host ""
if ($total -eq 0) {
    Write-Host "  Could not parse test results. Raw output:" -ForegroundColor Yellow
    foreach ($line in $clean) {
        if ($line.Trim()) { Write-Host "    $line" }
    }
} elseif ($failed.Count -eq 0) {
    Write-Host "  ALL $total TESTS PASSED" -ForegroundColor Green
} else {
    Write-Host "  $($passed.Count) PASSED, $($failed.Count) FAILED" -ForegroundColor Red
}
Write-Host ""
