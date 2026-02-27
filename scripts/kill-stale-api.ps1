# Kill any running TSIC.API process so dotnet build can overwrite DLLs
$found = $false

# Check for TSIC.API.exe (self-contained / .NET 6+ native exe)
Get-Process -Name 'TSIC.API' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Killing TSIC.API.exe (PID $($_.Id))"
    Stop-Process -Id $_.Id -Force
    $found = $true
}

# Check for dotnet.exe hosting TSIC.API.dll
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*TSIC.API*' } |
    ForEach-Object {
        Write-Host "Killing dotnet.exe hosting TSIC.API (PID $($_.ProcessId))"
        Stop-Process -Id $_.ProcessId -Force
        $found = $true
    }

if (-not $found) {
    Write-Host "No stale TSIC.API process found"
}

Write-Host "Ready for clean build"
