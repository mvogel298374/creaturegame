$dotnet = "C:\Users\USER\.dotnet\dotnet.exe"
$root   = $PSScriptRoot

# Set env vars in the child process so dotnet watch doesn't open its own browser tab
$backendCmd  = "`$env:ASPNETCORE_ENVIRONMENT='Development'; `$env:ASPNETCORE_URLS='http://localhost:5100'; & '$dotnet' watch run --project '$root\creaturegame.Web'"
$frontendCmd = "Set-Location '$root\creaturegame.Web\ClientApp'; npm run dev"

Start-Process pwsh -ArgumentList "-NoExit", "-Command", $backendCmd
Start-Process pwsh -ArgumentList "-NoExit", "-Command", $frontendCmd

Write-Host ""
Write-Host "Dev servers starting in new windows:"
Write-Host "  Backend : http://localhost:5100"
Write-Host "  Frontend: http://localhost:5173"
Write-Host ""
Write-Host "Waiting for frontend..."

$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    try {
        $null = Invoke-WebRequest -Uri "http://localhost:5173" -UseBasicParsing -TimeoutSec 1 -ErrorAction Stop
        $ready = $true
        break
    } catch { }
}

if ($ready) {
    Write-Host "Ready — opening http://localhost:5173"
    Start-Process "http://localhost:5173"
} else {
    Write-Host "Frontend did not respond in 60s. Open http://localhost:5173 manually."
}