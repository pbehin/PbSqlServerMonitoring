$apiUrl = "https://localhost:8701/api/connections"
$apiKey = "PB_MONITOR_API_KEY"

$body = @{
    name                   = "Lynx Proxy Test"
    server                 = "lynx.dnscdn.se,3341"
    database               = "master"
    username               = "peyman"
    password               = "GU6CPlZlXXPTwp98nBMX1"
    trustServerCertificate = $true 
    isEnabled              = $true
} | ConvertTo-Json

# Valid SSL bypass for PS 5.1
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
if ([System.Net.ServicePointManager]::SecurityProtocol -notmatch 'Tls12') {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
}

Write-Host "Adding connection to $apiUrl..."
try {
    $response = Invoke-RestMethod -Uri $apiUrl -Method Post -Body $body -ContentType "application/json" -Headers @{ "X-Api-Key" = "PB_MONITOR_API_KEY" }
    Write-Host "Success! Connection ID: $($response.id)"
}
catch {
    Write-Host "Error: $($_.Exception.Message)"
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "Details: $($reader.ReadToEnd())"
    }
}
