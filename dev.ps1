param(
    # Skip auto-opening the browser once the frontend is ready (the stack still starts and the URL is
    # printed). Handy when driving the app from tests/agents that don't want a tab popping up.
    [switch]$NoBrowser
)

# Resolve the SDK 9.0.200 dotnet: explicit override → user-local install → PATH (mirrors test.ps1).
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE }
          elseif (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" }
          else { 'dotnet' }
$root   = $PSScriptRoot

# Self-clean before starting: if a previous stack is still up (or left orphaned wrapper shells behind),
# tear it down first so repeated starts can't accumulate processes. stop-dev.ps1 is a no-op when nothing
# is running, and is scoped to this repo (never touches unrelated shells / MCP servers / build servers).
$portsBusy = @(5100, 5173 | Where-Object {
    Get-NetTCPConnection -LocalPort $_ -State Listen -ErrorAction SilentlyContinue
})
if ($portsBusy) {
    Write-Host "Existing dev stack detected on port(s) $($portsBusy -join ', ') - stopping it first..."
    & "$root\stop-dev.ps1" -Quiet | Out-Null
    Start-Sleep -Milliseconds 400
}

# Stop the backend from opening a :5100 browser tab. The launch profile (Properties/launchSettings.json)
# has launchBrowser:true, which the runtime honours on startup — so `--no-launch-profile` ignores the
# profile entirely (no launchBrowser, no applicationUrl); we set the URL + environment explicitly here
# instead. DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER additionally covers `dotnet watch`'s browser-refresh path.
# dev.ps1 opens the single :5173 tab itself (unless -NoBrowser).
$backendCmd  = "`$env:ASPNETCORE_ENVIRONMENT='Development'; `$env:ASPNETCORE_URLS='http://localhost:5100'; `$env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER='1'; & '$dotnet' watch run --no-launch-profile --project '$root\creaturegame.Web'"
$frontendCmd = "Set-Location '$root\creaturegame.Web\ClientApp'; npm run dev"

Start-Process pwsh -ArgumentList "-NoExit", "-Command", $backendCmd
Start-Process pwsh -ArgumentList "-NoExit", "-Command", $frontendCmd

Write-Host ""
Write-Host "Dev servers starting in new windows:"
Write-Host "  Backend : http://localhost:5100"
Write-Host "  Frontend: http://localhost:5173"
Write-Host ""
Write-Host "Waiting for frontend to become ready..."

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
    if ($NoBrowser) {
        Write-Host "Ready - http://localhost:5173 (browser not opened: -NoBrowser)"
    } else {
        Write-Host "Ready - opening http://localhost:5173"
        Start-Process "http://localhost:5173"
    }
} else {
    Write-Host "Frontend did not respond in 60s. Open http://localhost:5173 manually."
}
