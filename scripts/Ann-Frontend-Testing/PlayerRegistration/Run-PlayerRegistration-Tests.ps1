<#
.SYNOPSIS
    Runs Angular frontend tests for the Player Registration wizard.

.DESCRIPTION
    Validates payment logic, form validation, eligibility, team filtering,
    schema parsing, and waiver handling in the player registration wizard.

    Uses Vitest (Angular's test runner) -- no browser window needed.

.EXAMPLE
    .\Run-PlayerRegistration-Tests.ps1
#>

$ErrorActionPreference = "Stop"
$frontendDir = Join-Path $PSScriptRoot "..\..\..\TSIC-Core-Angular\src\frontend\tsic-app"

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "    Player Registration -- Frontend Tests" -ForegroundColor Cyan
Write-Host "    (payment / forms / eligibility / teams" -ForegroundColor Gray
Write-Host "     schema parsing / waivers)" -ForegroundColor Gray
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

$testPattern = "src/app/views/registration/player"

Write-Host "  Running tests..." -ForegroundColor Gray
Write-Host ""

Push-Location $frontendDir
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    $rawOutput = & npx ng test --include="$testPattern/**/*.spec.ts" --watch=false --reporters=verbose 2>&1 | ForEach-Object { $_.ToString() }
} finally {
    $ErrorActionPreference = $prevEAP
    Pop-Location
}

# Strip ANSI escape codes for clean regex matching
$clean = $rawOutput | ForEach-Object { $_ -replace '\x1b\[[0-9;]*m', '' }

# Parse individual test results from verbose output
# Format: " ✓  tsic-app  file.spec.ts > Suite > test name 24ms"
# Format: " ×  tsic-app  file.spec.ts > Suite > test name 24ms"
$passed = @(); $failed = @()
foreach ($line in $clean) {
    # Match lines with: tsic-app  <file>.spec.ts > <test description> <duration>ms
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
