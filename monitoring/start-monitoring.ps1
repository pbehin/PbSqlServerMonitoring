# Start Monitoring Stack using Docker Compose
# Run from monitoring/ directory

Write-Host "Starting SQL Server Monitoring Stack..." -ForegroundColor Cyan

# Ensure we are in the script's directory
Set-Location $PSScriptRoot

# 0. Load Environment Variables (Optional, docker-compose usually handles .env automatically)
if (Test-Path .env) {
    Write-Host "Loading .env file..." -ForegroundColor Green
    # No need to manually set env vars if docker-compose reads .env, but good for safety
}
else {
    Write-Host "Warning: .env file not found! Copy .env.example first." -ForegroundColor Yellow
}

# 1. Run Docker Compose
Write-Host "Running docker-compose up -d..."
docker-compose up -d

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

# 2. Run the .NET Application
Write-Host "`nStarting .NET Application..." -ForegroundColor Cyan
Set-Location $PSScriptRoot\..
Write-Host "Application: http://localhost:5000"
dotnet run
