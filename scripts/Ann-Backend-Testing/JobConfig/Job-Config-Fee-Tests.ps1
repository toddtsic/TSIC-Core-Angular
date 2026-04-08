<#
.SYNOPSIS
    Runs job config fee recalculation tests.

.DESCRIPTION
    Validates that toggling fee-affecting flags in Job Config Payment tab
    auto-recalculates team fees correctly. Tests both directions (on/off),
    processing fees, waitlist skipping, and paid team adjustments.

.EXAMPLE
    .\Job-Config-Fee-Tests.ps1
#>

$ErrorActionPreference = "Stop"
$testProject = Join-Path $PSScriptRoot "..\..\..\TSIC-Core-Angular\src\backend\TSIC.Tests\TSIC.Tests.csproj"

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "    Job Config Fee Recalculation Tests" -ForegroundColor Cyan
Write-Host "    (payment flag toggles)" -ForegroundColor Gray
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

$output = dotnet test $testProject --filter "FullyQualifiedName~JobConfig" --no-build --no-restore --verbosity normal 2>&1
$passed = @(); $failed = @()

foreach ($line in $output) {
    if ($line -match "^\s*Passed\s+(.+)\s+\[") { $passed += $Matches[1].Trim() }
    elseif ($line -match "^\s*Failed\s+(.+)\s+\[") { $failed += $Matches[1].Trim() }
}

if ($passed.Count -gt 0) {
    Write-Host "  PASSED:" -ForegroundColor Green
    foreach ($t in $passed) { Write-Host "    [PASS] $t" -ForegroundColor Green }
}
if ($failed.Count -gt 0) {
    Write-Host "  FAILED:" -ForegroundColor Red
    foreach ($t in $failed) { Write-Host "    [FAIL] $t" -ForegroundColor Red }
    Write-Host ""
    $inError = $false
    foreach ($line in $output) {
        if ($line -match "Error Message:") { $inError = $true }
        if ($inError -and $line -match "Stack Trace:") { $inError = $false }
        if ($inError) { Write-Host "    $line" -ForegroundColor Yellow }
    }
}

$total = $passed.Count + $failed.Count
Write-Host ""
if ($total -eq 0) {
    Write-Host "  No job config tests found — check build first!" -ForegroundColor Yellow
} elseif ($failed.Count -eq 0) {
    Write-Host "  ALL $total TESTS PASSED" -ForegroundColor Green
} else {
    Write-Host "  $($passed.Count) PASSED, $($failed.Count) FAILED" -ForegroundColor Red
}
Write-Host ""
