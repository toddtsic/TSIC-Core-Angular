<#
.SYNOPSIS
    Runs club-level payment allocation tests (search/teams, club scope).

.DESCRIPTION
    Validates how a single check or CC payment is distributed across multiple
    teams within a club. Tests highest-balance-first ordering, processing fee
    reductions per team, and club rep financial sync.

.EXAMPLE
    .\Run-Club-Allocation-Tests.ps1
#>

$ErrorActionPreference = "Stop"
$testProject = Join-Path $PSScriptRoot "..\..\..\TSIC-Core-Angular\src\backend\TSIC.Tests\TSIC.Tests.csproj"

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "    Club Payment Allocation Tests" -ForegroundColor Cyan
Write-Host "    (search/teams, club scope)" -ForegroundColor Gray
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

$job = Start-Job -ScriptBlock {
    param($proj)
    dotnet test $proj --filter "FullyQualifiedName~ClubAllocation" --no-restore --verbosity normal 2>&1
} -ArgumentList $testProject

$frames = @('|','/','-','\')
$i = 0
Write-Host -NoNewline "  Running tests  "
while ($job.State -eq 'Running') {
    Write-Host -NoNewline "`b$($frames[$i % 4])"
    $i++
    Start-Sleep -Milliseconds 150
}
Write-Host "`b "
$output = Receive-Job $job
Remove-Job $job

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
    Write-Host "  No club allocation tests found yet - coming soon!" -ForegroundColor Yellow
} elseif ($failed.Count -eq 0) {
    Write-Host "  ALL $total TESTS PASSED" -ForegroundColor Green
} else {
    Write-Host "  $($passed.Count) PASSED, $($failed.Count) FAILED" -ForegroundColor Red
}
Write-Host ""
