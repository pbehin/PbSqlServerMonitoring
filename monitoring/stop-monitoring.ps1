# Stop Monitoring Stack Manually
# Run from monitoring/ directory

Write-Host "Stopping SQL Server Monitoring Stack..." -ForegroundColor Cyan

# Ensure we are in the script's directory
Set-Location $PSScriptRoot

# List of containers to stop/remove
$containers = @("sql_exporter", "prometheus", "grafana", "sd_sidecar")

foreach ($c in $containers) {
    Write-Host "Removing container '$c'..."
    podman rm -f $c 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Removed '$c'." -ForegroundColor Gray
    }
    else {
        # Check if it didn't exist
        Write-Host "Container '$c' not found or already removed." -ForegroundColor DarkGray
    }
}

# Optional: Remove network (uncomment if desired, but usually good to keep)
# podman network rm monitoring 2>$null

Write-Host "`nStack stopped successfully." -ForegroundColor Green
