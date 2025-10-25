# Secure Deployment Script using PowerShell Remoting
# This script assumes PowerShell remoting is enabled on the target server

param(
    [string]$ServerIP = "10.0.0.45",
    [string]$ProjectRoot = "$PSScriptRoot\..",
    [PSCredential]$Credential = $null
)

Write-Host "=== Secure TSIC Deployment (PowerShell Remoting) ===" -ForegroundColor Green
Write-Host "Target Server: $ServerIP" -ForegroundColor Yellow
Write-Host ""

# Function to test PowerShell remoting
function Test-PSRemoting {
    param([string]$ServerIP, [PSCredential]$Credential)

    Write-Host "Testing PowerShell remoting to $ServerIP..." -ForegroundColor Cyan
    try {
        $session = New-PSSession -ComputerName $ServerIP -Credential $Credential -ErrorAction Stop
        Remove-PSSession $session
        Write-Host "✓ PowerShell remoting successful" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "✗ PowerShell remoting failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "To enable PS remoting on server, run: Enable-PSRemoting -Force" -ForegroundColor Yellow
        return $false
    }
}

# Function to deploy via remoting
function Deploy-ViaRemoting {
    param([string]$ServerIP, [PSCredential]$Credential)

    Write-Host "=== Deploying via PowerShell Remoting ===" -ForegroundColor Magenta

    $ApiSource = Join-Path $ProjectRoot "publish\api"
    $AngularSource = Join-Path $ProjectRoot "publish\angular"

    # Check if build outputs exist
    if (!(Test-Path $ApiSource)) {
        Write-Error "API build output not found: $ApiSource"
        return $false
    }

    if (!(Test-Path $AngularSource)) {
        Write-Error "Angular build output not found: $AngularSource"
        return $false
    }

    try {
        # Create remote session
        $session = New-PSSession -ComputerName $ServerIP -Credential $Credential

        # Create directories on remote server
        Invoke-Command -Session $session -ScriptBlock {
            if (!(Test-Path "C:\inetpub\wwwroot\tsic-api")) {
                New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\tsic-api" -Force
            }
            if (!(Test-Path "C:\inetpub\wwwroot\tsic-app")) {
                New-Item -ItemType Directory -Path "C:\inetpub\wwwroot\tsic-app" -Force
            }
        }

        # Copy files using Copy-Item with -ToSession parameter
        Write-Host "Copying API files..." -ForegroundColor Cyan
        Copy-Item "$ApiSource\*" -Destination "C:\inetpub\wwwroot\tsic-api" -ToSession $session -Recurse -Force

        Write-Host "Copying Angular files..." -ForegroundColor Cyan
        Copy-Item "$AngularSource\*" -Destination "C:\inetpub\wwwroot\tsic-app" -ToSession $session -Recurse -Force

        # Copy web.config files
        $ApiConfig = Join-Path $PSScriptRoot "web.config.api"
        $AngularConfig = Join-Path $PSScriptRoot "web.config.angular"

        if (Test-Path $ApiConfig) {
            Copy-Item $ApiConfig -Destination "C:\inetpub\wwwroot\tsic-api\web.config" -ToSession $session -Force
        }

        if (Test-Path $AngularConfig) {
            Copy-Item $AngularConfig -Destination "C:\inetpub\wwwroot\tsic-app\web.config" -ToSession $session -Force
        }

        # Clean up session
        Remove-PSSession $session

        Write-Host "✓ Secure deployment completed successfully!" -ForegroundColor Green
        return $true

    } catch {
        Write-Error "Deployment failed: $_"
        if ($session) { Remove-PSSession $session }
        return $false
    }
}

# Main deployment flow
try {
    # Get credentials if not provided
    if (!$Credential) {
        $Credential = Get-Credential -Message "Enter credentials for $ServerIP (must be administrator)"
    }

    # Test PowerShell remoting
    if (!(Test-PSRemoting -ServerIP $ServerIP -Credential $Credential)) {
        Write-Host ""
        Write-Host "PowerShell remoting is not available. Alternatives:" -ForegroundColor Yellow
        Write-Host "1. Enable PS remoting on server: Enable-PSRemoting -Force" -ForegroundColor White
        Write-Host "2. Use the manual deployment method from Deploy-TSIC.ps1" -ForegroundColor White
        Write-Host "3. Copy files manually via RDP or shared folder" -ForegroundColor White
        exit 1
    }

    # Deploy via remoting
    if (!(Deploy-ViaRemoting -ServerIP $ServerIP -Credential $Credential)) {
        exit 1
    }

    Write-Host ""
    Write-Host "=== Deployment Summary ===" -ForegroundColor Green
    Write-Host "✓ .NET API deployed to: http://$ServerIP/tsic-api" -ForegroundColor Green
    Write-Host "✓ Angular app deployed to: http://$ServerIP/tsic-app" -ForegroundColor Green

} catch {
    Write-Error "Deployment failed with error: $_"
    exit 1
}