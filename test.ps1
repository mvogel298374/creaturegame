#requires -Version 7
<#
.SYNOPSIS
  Runs every test suite in the solution and prints one summary:
    • .NET unit tests (xUnit)      — tests/creaturegame.Tests
    • Frontend unit tests (Vitest) — creaturegame.Web/ClientApp  (src/**/*.test.ts)
    • Frontend E2E (Playwright)    — creaturegame.Web/ClientApp/e2e

  Exits non-zero if any suite fails, so it's usable in CI.

.DESCRIPTION
  With no switches, runs all three. The Playwright suite needs the app running
  (Vite :5173 proxying to the backend :5100); if the backend isn't detected it is
  skipped with a notice, unless -StartStack is given (then the backend is started
  for the run and stopped afterwards; Playwright starts/stops Vite itself).

.EXAMPLE
  .\test.ps1                 # all suites (E2E skipped if the stack isn't up)
.EXAMPLE
  .\test.ps1 -Dotnet         # only .NET unit tests
.EXAMPLE
  .\test.ps1 -Web            # only Vitest
.EXAMPLE
  .\test.ps1 -E2E -StartStack  # only Playwright, starting/stopping the backend itself
#>
[CmdletBinding()]
param(
  [switch]$Dotnet,
  [switch]$Web,
  [switch]$E2E,
  [switch]$StartStack
)

$root      = $PSScriptRoot
$clientApp = Join-Path $root 'creaturegame.Web\ClientApp'
$dotnetExe    = if ($env:DOTNET_EXE) { $env:DOTNET_EXE }
             elseif (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" }
             else { 'dotnet' }

# No specific suite requested → run everything.
$runAll = -not ($Dotnet -or $Web -or $E2E)
$results = [ordered]@{}

function Test-Backend {
  try { return (Invoke-WebRequest 'http://localhost:5100/api/Species' -UseBasicParsing -TimeoutSec 2).StatusCode -ge 200 }
  catch { return $false }
}

# ── .NET unit tests ──────────────────────────────────────────────────────────
if ($Dotnet -or $runAll) {
  Write-Host "`n=== .NET unit tests (xUnit) ===" -ForegroundColor Cyan
  & $dotnetExe test (Join-Path $root 'tests\creaturegame.Tests') --nologo
  $results['.NET (xUnit)'] = ($LASTEXITCODE -eq 0)
}

# ── Frontend unit tests (Vitest) ─────────────────────────────────────────────
if ($Web -or $runAll) {
  Write-Host "`n=== Frontend unit tests (Vitest) ===" -ForegroundColor Cyan
  Push-Location $clientApp
  try { npm test; $results['Vitest'] = ($LASTEXITCODE -eq 0) }
  finally { Pop-Location }
}

# ── Frontend E2E (Playwright) ────────────────────────────────────────────────
if ($E2E -or $runAll) {
  $backend = $null
  $startedBackend = $false
  try {
    if (-not (Test-Backend) -and $StartStack) {
      Write-Host "`nStarting backend on :5100 for E2E..." -ForegroundColor DarkCyan
      $backend = Start-Process $dotnetExe -ArgumentList 'run','--project',(Join-Path $root 'creaturegame.Web') `
                   -PassThru -WindowStyle Hidden
      $startedBackend = $true
      $deadline = (Get-Date).AddSeconds(60)
      while (-not (Test-Backend) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    }

    if (-not (Test-Backend)) {
      Write-Host "`n=== E2E (Playwright) — SKIPPED ===" -ForegroundColor Yellow
      Write-Host "  Backend not reachable on :5100. Start it with .\dev.ps1 (or pass -StartStack)." -ForegroundColor Yellow
      $results['Playwright E2E'] = $null   # skipped
    } else {
      Write-Host "`n=== E2E (Playwright) ===" -ForegroundColor Cyan
      Push-Location $clientApp
      try { npm run test:e2e; $results['Playwright E2E'] = ($LASTEXITCODE -eq 0) }
      finally { Pop-Location }
    }
  }
  finally {
    if ($startedBackend -and $backend -and -not $backend.HasExited) {
      Write-Host "Stopping backend started for E2E..." -ForegroundColor DarkCyan
      Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue
    }
  }
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host "`n================ TEST SUMMARY ================" -ForegroundColor Cyan
$anyFail = $false
foreach ($k in $results.Keys) {
  $v = $results[$k]
  if     ($null -eq $v) { Write-Host ("  {0,-16} SKIPPED" -f $k) -ForegroundColor Yellow }
  elseif ($v)           { Write-Host ("  {0,-16} PASS"    -f $k) -ForegroundColor Green }
  else                  { Write-Host ("  {0,-16} FAIL"    -f $k) -ForegroundColor Red; $anyFail = $true }
}
Write-Host "==============================================" -ForegroundColor Cyan

if ($anyFail) { exit 1 } else { exit 0 }
