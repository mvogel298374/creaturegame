#requires -Version 7
<#
.SYNOPSIS
  Produce a single self-contained Release build that serves the React frontend,
  the REST API, and the SignalR hub from one ASP.NET Core process.

.DESCRIPTION
  Steps:
    1. Build the frontend (tsc + vite build) -> creaturegame.Web/ClientApp/dist
    2. Copy the build into creaturegame.Web/wwwroot, preserving the static
       audio/ and sprites/ directories the backend already serves.
    3. dotnet publish creaturegame.Web -> ./publish

  Run the result with:
      dotnet publish/creaturegame.Web.dll
  then open http://localhost:5000 (or the URL Kestrel prints).

.PARAMETER Configuration
  Build configuration passed to dotnet publish. Default: Release.

.PARAMETER OutputDir
  Publish output directory (relative to repo root). Default: publish.

.PARAMETER SkipFrontend
  Skip the npm build + wwwroot copy; only run dotnet publish. Useful when the
  frontend is already built and you are iterating on the backend.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDir     = 'publish',
    [switch]$SkipFrontend
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
Set-Location $repoRoot

# global.json pins SDK 9.0.200; prefer the user-local SDK if the system dotnet is runtime-only.
$dotnet = if (Test-Path "$HOME\.dotnet\dotnet.exe") { "$HOME\.dotnet\dotnet.exe" } else { 'dotnet' }

$web       = Join-Path $repoRoot 'creaturegame.Web'
$clientApp = Join-Path $web 'ClientApp'
$wwwroot   = Join-Path $web 'wwwroot'
$dist      = Join-Path $clientApp 'dist'

if (-not $SkipFrontend) {
    Write-Host '==> Building frontend (tsc + vite build)...' -ForegroundColor Cyan
    Push-Location $clientApp
    try {
        if (-not (Test-Path 'node_modules')) {
            Write-Host '    node_modules missing - running npm ci' -ForegroundColor Yellow
            npm ci
            if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
        }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'frontend build failed' }
    }
    finally { Pop-Location }

    Write-Host '==> Copying frontend into wwwroot (preserving audio/ + sprites/)...' -ForegroundColor Cyan
    # Remove only the previously-published SPA assets, never the static audio/sprites dirs.
    Remove-Item (Join-Path $wwwroot 'index.html') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $wwwroot 'assets') -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $dist '*') $wwwroot -Recurse -Force
}

Write-Host "==> dotnet publish ($Configuration) -> $OutputDir ..." -ForegroundColor Cyan
& $dotnet publish $web -c $Configuration -o (Join-Path $repoRoot $OutputDir)
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host ''
Write-Host '==> Release build complete.' -ForegroundColor Green
Write-Host "    Run it with:  $dotnet $OutputDir/creaturegame.Web.dll"
Write-Host '    Then open the URL Kestrel prints (default http://localhost:5000).'
