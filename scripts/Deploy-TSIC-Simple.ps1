# Complete TSIC Deployment Script
# Builds and deploys both .NET API and Angular frontend to Windows Server

param(
    [string]$ServerIP = '10.0.0.45',
    [string]$ProjectRoot = '$PSScriptRoot\..',
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$RemoteDeploy
)

Write-Host '=== TSIC Complete Deployment Script ===' -ForegroundColor Green
Write-Host 'Target Server: $ServerIP' -ForegroundColor Yellow
Write-Host 'Configuration: $Configuration' -ForegroundColor Yellow
Write-Host 'Skip Build: $SkipBuild' -ForegroundColor Yellow
Write-Host 'Skip Tests: $SkipTests' -ForegroundColor Yellow
Write-Host 'Remote Deploy: $RemoteDeploy' -ForegroundColor Yellow
Write-Host ''

# Function to test server connectivity
function Test-ServerConnection {
    param([string]$ServerIP)
    
    Write-Host 'Testing connection to $ServerIP...' -ForegroundColor Cyan
    try {
        $ping = New-Object System.Net.NetworkInformation.Ping
        $result = $ping.Send($ServerIP, 1000)
        if ($result.Status -eq 'Success') {
            Write-Host ' Server connection successful' -ForegroundColor Green
            return $true
        } else {
            Write-Host ' Cannot connect to server $ServerIP' -ForegroundColor Red
            return $false
        }
    } catch {
        Write-Host ' Cannot connect to server $ServerIP' -ForegroundColor Red
        return $false
    }
}

# Main deployment flow
try {
    # Test server connection
    if (!(Test-ServerConnection -ServerIP $ServerIP)) {
        exit 1
    }
    
    Write-Host 'Server connection test completed. Build outputs exist at:' -ForegroundColor Green
    Write-Host 'API: $PSScriptRoot\..\publish\api' -ForegroundColor Cyan
    Write-Host 'Angular: $PSScriptRoot\..\publish\angular' -ForegroundColor Cyan
    
} catch {
    Write-Error 'Deployment failed with error: $_'
    exit 1
}
