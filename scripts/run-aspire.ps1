Param()

Push-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)
Try {
    $composeFile = Join-Path $PSScriptRoot "..\tools\aspire\docker-compose.aspire.yml"
    if (-Not (Test-Path $composeFile)) {
        Write-Error "Compose file not found: $composeFile"
        exit 1
    }

    Write-Host "Running Aspire docker-compose ($composeFile) ..."
    # Use Docker Compose v2+ (docker compose) if available, fall back to docker-compose
    if (Get-Command 'docker' -ErrorAction SilentlyContinue) {
        & docker compose -f $composeFile up --build
    } else {
        Write-Error "Docker CLI not found in PATH. Install Docker Desktop or Docker CLI to run Aspire compose."
        exit 1
    }
} Finally {
    Pop-Location
}
