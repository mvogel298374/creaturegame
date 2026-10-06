param(
    # Skip auto-opening the browser once the frontend is ready (the stack still starts and the URL is
    # printed). Handy when driving the app from tests/agents that don't want a tab popping up.
    [switch]$NoBrowser,
    # How long to wait for BOTH servers before giving up (exit 1). A cold backend build can need more than 60.
    [int]$TimeoutSeconds = 60
)

# Resolve the SDK 9.0.200 dotnet: explicit override → user-local install → PATH (mirrors test.ps1).
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE }
          elseif (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" }
          else { 'dotnet' }
$root   = $PSScriptRoot

# Self-clean before starting: tear down a previous stack AND any orphaned -NoExit wrapper windows (a crashed
# server leaves its window behind holding no port). stop-dev.ps1 is a no-op when nothing is running, and is
# scoped to this repo (never touches unrelated shells / MCP servers / build servers, or foreign port holders).
if (& "$root\stop-dev.ps1" -Quiet) {
    Write-Host "Stopped a previous dev stack."
    Start-Sleep -Milliseconds 400
}

# stop-dev leaves processes that aren't from this repo alone; if one still holds a dev port, starting would
# either fail to bind or (Vite) quietly move to another port while the readiness wait passes against the squatter.
$squatters = foreach ($port in 5100, 5173) {
    foreach ($id in (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique)) {
        $name = (Get-Process -Id $id -ErrorAction SilentlyContinue).ProcessName ?? '?'
        "  port $port is held by PID $id ($name)"
    }
}
if ($squatters) {
    Write-Host "Not starting: a dev port is still held after cleanup (stop-dev only stops this repo's processes):" -ForegroundColor Red
    $squatters | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "Free the port(s) and re-run." -ForegroundColor Red
    exit 1
}

# Stop the backend from opening a :5100 browser tab. The launch profile (Properties/launchSettings.json)
# has launchBrowser:true, which the runtime honours on startup — so `--no-launch-profile` ignores the
# profile entirely (no launchBrowser, no applicationUrl); we set the URL + environment explicitly here
# instead. DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER additionally covers `dotnet watch`'s browser-refresh path.
# dev.ps1 opens the single :5173 tab itself (unless -NoBrowser).
#
# Each server's output is also teed to .dev/*.log (git-ignored, reset every start) so a failed start can be read
# without the windows. UTF-8 first: piped native output is otherwise decoded with the console code page and the
# emoji/box characters garble. Piping drops colour (the servers no longer see a TTY).
$logDir      = Join-Path $root '.dev'
$backendLog  = Join-Path $logDir 'backend.log'
$frontendLog = Join-Path $logDir 'frontend.log'
New-Item -ItemType Directory -Force $logDir | Out-Null
$utf8        = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; "
$backendCmd  = $utf8 + "`$env:ASPNETCORE_ENVIRONMENT='Development'; `$env:ASPNETCORE_URLS='http://localhost:5100'; `$env:DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER='1'; & '$dotnet' watch run --no-launch-profile --project '$root\creaturegame.Web' 2>&1 | Tee-Object -FilePath '$backendLog'"
$frontendCmd = $utf8 + "Set-Location '$root\creaturegame.Web\ClientApp'; npm run dev 2>&1 | Tee-Object -FilePath '$frontendLog'"

Start-Process pwsh -ArgumentList "-NoExit", "-Command", $backendCmd
Start-Process pwsh -ArgumentList "-NoExit", "-Command", $frontendCmd

Write-Host ""
Write-Host "Dev servers starting in new windows:"
Write-Host "  Backend : http://localhost:5100"
Write-Host "  Frontend: http://localhost:5173"
Write-Host ""
Write-Host "Waiting for backend and frontend (up to ${TimeoutSeconds}s; logs in $logDir)..."

# Any HTTP response counts as up (even a 404); only a refused/timed-out connection means still starting.
function Test-Up([string]$url) {
    try {
        $null = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 1 -SkipHttpErrorCheck -ErrorAction Stop
        return $true
    } catch { return $false }
}

$backendUrl  = 'http://localhost:5100/api/dev/status'
$frontendUrl = 'http://localhost:5173'
$backendUp   = $false
$frontendUp  = $false
for ($i = 0; $i -lt $TimeoutSeconds -and -not ($backendUp -and $frontendUp); $i++) {
    Start-Sleep -Seconds 1
    if (-not $backendUp)  { $backendUp  = Test-Up $backendUrl }
    if (-not $frontendUp) { $frontendUp = Test-Up $frontendUrl }
}

if ($backendUp -and $frontendUp) {
    if ($NoBrowser) {
        Write-Host "Ready - http://localhost:5173 (browser not opened: -NoBrowser)"
    } else {
        Write-Host "Ready - opening http://localhost:5173"
        Start-Process "http://localhost:5173"
    }
    exit 0
}

$down = @()
if (-not $backendUp)  { $down += "backend ($backendUrl)" }
if (-not $frontendUp) { $down += "frontend ($frontendUrl)" }
Write-Host "Not ready after ${TimeoutSeconds}s - no response from: $($down -join ', ')" -ForegroundColor Red
Write-Host "Logs: $backendLog" -ForegroundColor Red
Write-Host "      $frontendLog" -ForegroundColor Red
exit 1
