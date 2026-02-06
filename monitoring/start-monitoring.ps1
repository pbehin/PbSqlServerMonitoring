# Start Monitoring Stack using Docker Compose
# Run from monitoring/ directory

Write-Host "Starting SQL Server Monitoring Stack..." -ForegroundColor Cyan

# Ensure we are in the script's directory
Set-Location $PSScriptRoot

# 0. Load Environment Variables
if (Test-Path .env) {
    Write-Host "Loading .env file..." -ForegroundColor Green
}
else {
    Write-Host "Warning: .env file not found! Copy .env.example first." -ForegroundColor Yellow
}

# 1. Check Container Engine (Podman or Docker)
$containerCmd = "docker-compose"
$containerTool = "Docker"

if (Get-Command "podman" -ErrorAction SilentlyContinue) {
    Write-Host "Podman detected." -ForegroundColor Cyan
    $containerTool = "Podman"
    
    # Check if podman-compose is installed, otherwise use podman compose (v4+)
    if (Get-Command "podman-compose" -ErrorAction SilentlyContinue) {
        $containerCmd = "podman-compose"
    }
    else {
        # Check if 'podman compose' works
        $composeCheck = podman compose --help 2>&1
        if ($LASTEXITCODE -eq 0) {
            $containerCmd = "podman compose"
        }
    }
}
elseif (Get-Command "docker-compose" -ErrorAction SilentlyContinue) {
    Write-Host "Docker Compose detected." -ForegroundColor Cyan
}
else {
    # Fallback for newer Docker Desktop versions that use 'docker compose' check
    $dockerComposeCheck = docker compose version 2>&1
    if ($LASTEXITCODE -eq 0) {
        $containerCmd = "docker compose"
        Write-Host "Docker Compose (plugin) detected." -ForegroundColor Cyan
    }
    else {
        Write-Host "Error: Neither podman-compose, podman compose, nor docker-compose found." -ForegroundColor Red
        exit 1
    }
}

# 2. Check if Engine is Running
Write-Host "Checking if $containerTool is running..."
if ($containerTool -eq "Podman") {
    $info = podman info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Podman is not running or machine not started." -ForegroundColor Red
        Write-Host "Try running: podman machine start"
        exit 1
    }
}
else {
    $info = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Docker is not running." -ForegroundColor Red
        exit 1
    }
}

# 3. Run Compose
Write-Host "Running $containerCmd up -d --build..."
Invoke-Expression "$containerCmd up -d --build"

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nStack started successfully!" -ForegroundColor Green
    Write-Host "Grafana: http://localhost:3000"
    Write-Host "Prometheus: http://localhost:9090"
    Write-Host "Metrics: http://localhost:9399/metrics"
}
else {
    Write-Host "Failed to start stack." -ForegroundColor Red
    exit $LASTEXITCODE
}

# 4. Run the .NET Application
Write-Host "`nStarting .NET Application..." -ForegroundColor Cyan
Set-Location $PSScriptRoot\..
Write-Host "Application: https://localhost:8701"
dotnet run
