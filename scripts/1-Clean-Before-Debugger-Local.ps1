# 1-Clean-Before-Debugger-Local.ps1
# Run AFTER stopping debugger (Shift+F5), BEFORE hitting F5 again.
# Only kills build processes (dotnet, compiler) - NOT the C# extension.

$ErrorActionPreference = 'SilentlyContinue'

$apiDir = Join-Path $PSScriptRoot '..\TSIC-Core-Angular\src\backend\TSIC.API'

# 1. Kill build processes that lock bin/obj (NOT C# extension)
$buildProcs = @('dotnet', 'VBCSCompiler')
$killed = 0
foreach ($name in $buildProcs) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($procs) {
        $killed += $procs.Count
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

if ($killed -gt 0) {
    Write-Host "[1/2] Killed $killed build process(es). Waiting..." -ForegroundColor Yellow
    Start-Sleep -Seconds 2
} else {
    Write-Host "[1/2] No build processes found." -ForegroundColor Green
}

# 2. Clear bin/obj for the API project
$binDir = Join-Path $apiDir 'bin'
$objDir = Join-Path $apiDir 'obj'

Write-Host "[2/2] Clearing API bin/obj..." -ForegroundColor Yellow
$failed = $false

if (Test-Path $binDir) {
    Remove-Item $binDir -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $binDir) { $failed = $true }
}
if (Test-Path $objDir) {
    Remove-Item $objDir -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $objDir) { $failed = $true }
}

if ($failed) {
    Write-Host "       FAILED - files still locked." -ForegroundColor Red
    Write-Host "       Try: Ctrl+Shift+P > Reload Window, then run again." -ForegroundColor Red
    exit 1
} else {
    Write-Host "       Done." -ForegroundColor Green
}

Write-Host ""
Write-Host "Clean! Hit F5 to build and debug." -ForegroundColor Green
exit 0
