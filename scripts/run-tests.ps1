<#
.SYNOPSIS
    Run the TSIC test suite from anywhere in the repo.

.DESCRIPTION
    The solution sits one level down, in TSIC-Core-Angular\TSIC-Core-Angular.sln, so
    `dotnet test` from the repo root fails with MSB1003. This resolves the solution from the
    script's own location, so the working directory stops mattering.

    Pass -Filter for a slice, or nothing for the whole suite.

.PARAMETER Filter
    Substring matched against fully-qualified test names. A class name ("TeamPushScopeTests")
    or a namespace ("TSIC.Tests.Mobile") both work.

.PARAMETER Configuration
    Build configuration. Debug by default.

.PARAMETER NoBuild
    Skip the build and run whatever is already compiled. Fast when nothing changed.

.PARAMETER DryRun
    Print the dotnet command and exit without running it.

.EXAMPLE
    .\scripts\run-tests.ps1
    Whole suite.

.EXAMPLE
    .\scripts\run-tests.ps1 -Filter TeamPushScopeTests
    One class.

.EXAMPLE
    .\scripts\run-tests.ps1 -Filter TSIC.Tests.Mobile
    Every mobile test - the slice that shares MobileDataBuilder.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Filter,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$NoBuild,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "TSIC-Core-Angular\TSIC-Core-Angular.sln"

if (-not (Test-Path $solution)) {
    Write-Error "Solution not found at $solution"
    exit 1
}

$dotnetArgs = @("test", $solution, "--configuration", $Configuration, "--nologo")
if ($Filter)  { $dotnetArgs += @("--filter", "FullyQualifiedName~$Filter") }
if ($NoBuild) { $dotnetArgs += "--no-build" }

Write-Host ""
Write-Host "solution : $solution" -ForegroundColor DarkGray
if ($Filter) {
    Write-Host "filter   : FullyQualifiedName~$Filter" -ForegroundColor Yellow
} else {
    Write-Host "filter   : (none - full suite)" -ForegroundColor Yellow
}
Write-Host ""

if ($DryRun) {
    Write-Host "dotnet $($dotnetArgs -join ' ')" -ForegroundColor Cyan
    exit 0
}

& dotnet @dotnetArgs
$exit = $LASTEXITCODE

Write-Host ""
if ($exit -eq 0) {
    Write-Host "PASSED" -ForegroundColor Green
} else {
    # A red test in this repo is real - the suite has been kept green, so a failure is a
    # finding rather than noise.
    Write-Host "FAILED (exit $exit) - scroll up for the failing test names." -ForegroundColor Red
}
Write-Host ""

exit $exit
