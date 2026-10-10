#requires -Version 7
<#
.SYNOPSIS
  Runs every test suite in the solution and prints a verbose, aggregated summary:
    • .NET unit tests (xUnit)      — tests/creaturegame.Tests
    • Frontend typecheck (tsc)     — creaturegame.Web/ClientApp  (src/ + e2e/; Vitest can't catch this)
    • Frontend unit tests (Vitest) — creaturegame.Web/ClientApp  (src/**/*.test.ts)
    • Frontend E2E (Playwright)    — creaturegame.Web/ClientApp/e2e

  Per suite it reports passed/total counts and, on failure, the names of the
  failing tests. Exits non-zero if any suite fails, so it's usable in CI.

.DESCRIPTION
  With no switches, runs all three. The Playwright suite needs the app running
  (Vite :5173 proxying to the backend :5100); if the backend isn't detected it is
  skipped with a notice, unless -StartStack is given (then the backend is started
  for the run and stopped afterwards; Playwright starts/stops Vite itself).

.EXAMPLE
  .\test.ps1                   # all suites (E2E skipped unless the backend is already up, or -StartStack)
.EXAMPLE
  .\test.ps1 -Dotnet           # only .NET unit tests
.EXAMPLE
  .\test.ps1 -E2E -StartStack  # only Playwright, starting/stopping the backend itself
.EXAMPLE
  .\test.ps1 -Filter AbandonTimerTests   # only .NET tests whose name contains it (implies -Dotnet)
.EXAMPLE
  .\test.ps1 -Dotnet -Filter "FullyQualifiedName~Bag&Category=Unit"   # a full dotnet --filter expression, passed as-is
#>
[CmdletBinding()]
param(
  [switch]$Dotnet,
  [switch]$Web,
  [switch]$E2E,
  [switch]$StartStack,
  [string]$Filter
)

# Playwright/npm/dotnet write UTF-8 (› ✓ ✘ …), but PowerShell decodes native-command output with the console's
# OEM code page, so on Windows those show as mojibake ("ÔÇ║", "Ô£ô"). Decode as UTF-8 for the run and put the
# caller's encoding back at the end — it's process-wide, and this script runs in the caller's own session.
$prevOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$root      = $PSScriptRoot
$clientApp = Join-Path $root 'creaturegame.Web\ClientApp'
$dotnetExe = if ($env:DOTNET_EXE) { $env:DOTNET_EXE }
             elseif (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" }
             else { 'dotnet' }

# -Filter alone means "this subset of the .NET suite", never "everything"; with -Web/-E2E only it is ignored.
if ($Filter -and -not ($Dotnet -or $Web -or $E2E)) { $Dotnet = $true }
if ($Filter -and -not $Dotnet) { Write-Host "Note: -Filter applies to the .NET suite only; ignored." -ForegroundColor Yellow }

# No specific suite requested → run everything.
$runAll = -not ($Dotnet -or $Web -or $E2E)

# A bare word is a name match; anything carrying a dotnet filter operator is passed through untouched.
$dotnetFilterArgs = @()
if ($Filter) {
  $expr = if ($Filter -match '[=~!|&()]') { $Filter } else { "FullyQualifiedName~$Filter" }
  $dotnetFilterArgs = @('--filter', $expr)
}
$results = [ordered]@{}

function Test-Backend {
  try { return (Invoke-WebRequest 'http://localhost:5100/api/Species' -UseBasicParsing -TimeoutSec 2).StatusCode -ge 200 }
  catch { return $false }
}

# $Detail overrides the "N/M passed" summary text for a suite that isn't count-shaped (the typecheck is
# pass/fail, not a tally) — everything else leaves it null and reports counts as before.
function New-Result {
  param($Status, $Passed = 0, $Failed = 0, $Skipped = 0, $Total = 0, $FailedNames = @(), $Detail = $null)
  [pscustomobject]@{ Status = $Status; Passed = $Passed; Failed = $Failed; Skipped = $Skipped; Total = $Total; FailedNames = @($FailedNames); Detail = $Detail }
}

# Test runners emit ANSI colour codes even when piped, which breaks `\s+`-based
# regexes — strip them before parsing.
function Strip-Ansi([object[]]$lines) {
  @($lines | ForEach-Object { ("$_" -replace "`e\[[0-9;]*[a-zA-Z]", '') })
}

# ── .NET unit tests ──────────────────────────────────────────────────────────
if ($Dotnet -or $runAll) {
  Write-Host "`n=== .NET unit tests (xUnit) ===" -ForegroundColor Cyan
  & $dotnetExe test (Join-Path $root 'tests\creaturegame.Tests') --nologo @dotnetFilterArgs 2>&1 | Tee-Object -Variable raw
  $ok = ($LASTEXITCODE -eq 0)
  $lines = Strip-Ansi $raw; $text = ($lines -join "`n")
  $passed = $failed = $skipped = $total = 0
  if ($text -match '-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)') {
    $failed = [int]$Matches[1]; $passed = [int]$Matches[2]; $skipped = [int]$Matches[3]; $total = [int]$Matches[4]
  }
  $names = @($lines | ForEach-Object { if ($_ -match '^\s*Failed\s+(.+?)\s+\[') { $Matches[1] } })
  # dotnet exits 0 when a filter matches nothing; that must not read as a pass.
  $noMatch = ($Filter -and $total -eq 0)
  $results['.NET (xUnit)'] = New-Result -Status (($ok -and -not $noMatch) ? 'PASS' : 'FAIL') -Passed $passed -Failed $failed -Skipped $skipped -Total $total -FailedNames $names `
    -Detail ($noMatch ? "0/0 - filter '$Filter' matched no tests" : $null)
}

# ── Frontend typecheck (tsc) ─────────────────────────────────────────────────
# Vitest transpiles via esbuild, which strips types WITHOUT checking them, so nothing below catches a type
# error — `tsc` otherwise runs only in `npm run build`. Runs before Vitest so a type break is reported as
# itself rather than as a confusing test failure. Covers src/ + e2e/ per tsconfig.
if ($Web -or $runAll) {
  Write-Host "`n=== Frontend typecheck (tsc) ===" -ForegroundColor Cyan
  Push-Location $clientApp
  try { npm run typecheck 2>&1 | Tee-Object -Variable raw } finally { Pop-Location }
  $ok = ($LASTEXITCODE -eq 0)
  $lines = Strip-Ansi $raw
  # tsc prints one line per error: `src/foo.ts(12,3): error TS2345: <message>`.
  $names = @($lines | ForEach-Object { if ($_ -match '^\s*(\S+\(\d+,\d+\):\s*error\s+TS\d+:.+)$') { ($Matches[1]).Trim() } })
  $errs  = $names.Count
  $results['TypeScript'] = New-Result -Status ($ok ? 'PASS' : 'FAIL') -Failed $errs -FailedNames $names `
    -Detail ($ok ? 'no type errors' : ("{0} type error(s)" -f $errs))
}

# ── Frontend unit tests (Vitest) ─────────────────────────────────────────────
if ($Web -or $runAll) {
  Write-Host "`n=== Frontend unit tests (Vitest) ===" -ForegroundColor Cyan
  Push-Location $clientApp
  try { npm test 2>&1 | Tee-Object -Variable raw } finally { Pop-Location }
  $ok = ($LASTEXITCODE -eq 0)
  $lines = Strip-Ansi $raw; $text = ($lines -join "`n")
  $passed = $failed = $skipped = $total = 0
  if ($text -match 'Tests\s+(?:(\d+)\s+failed\s*\|\s*)?(\d+)\s+passed') { $failed = [int]($Matches[1]); $passed = [int]$Matches[2] }
  if ($text -match 'Tests\s+[^\r\n]*\((\d+)\)') { $total = [int]$Matches[1] }
  $names = @($lines | ForEach-Object { if ($_ -match '^\s*(?:FAIL|×|✗)\s+(.+)$') { ($Matches[1]).Trim() } })
  $results['Vitest'] = New-Result -Status ($ok ? 'PASS' : 'FAIL') -Passed $passed -Failed $failed -Skipped $skipped -Total $total -FailedNames $names
}

# ── Frontend E2E (Playwright) ────────────────────────────────────────────────
if ($E2E -or $runAll) {
  $startedBackend = $false; $backend = $null
  try {
    if (-not (Test-Backend) -and $StartStack) {
      Write-Host "`nStarting backend on :5100 for E2E..." -ForegroundColor DarkCyan
      $env:DevMode__Enabled = 'true' # E2E specs rely on Dev Mode (?forceDraft=1) — see e2e.ps1
      $backend = Start-Process $dotnetExe -ArgumentList 'run','--project',(Join-Path $root 'creaturegame.Web') -PassThru -WindowStyle Hidden
      $startedBackend = $true
      $deadline = (Get-Date).AddSeconds(60)
      while (-not (Test-Backend) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    }

    if ($startedBackend -and -not (Test-Backend)) {
      Write-Host "`n=== E2E (Playwright) — FAILED ===" -ForegroundColor Red
      Write-Host "  Backend did not come up on :5100 within 60s." -ForegroundColor Red
      $results['Playwright E2E'] = New-Result -Status 'FAIL' -Detail 'backend failed to start'
    } elseif (-not (Test-Backend)) {
      Write-Host "`n=== E2E (Playwright) — SKIPPED ===" -ForegroundColor Yellow
      Write-Host "  Backend not running. Pass -StartStack." -ForegroundColor Yellow
      $results['Playwright E2E'] = New-Result -Status 'SKIPPED'
    } else {
      Write-Host "`n=== E2E (Playwright) ===" -ForegroundColor Cyan
      Push-Location $clientApp
      try { npm run test:e2e 2>&1 | Tee-Object -Variable raw } finally { Pop-Location }
      $ok = ($LASTEXITCODE -eq 0)
      $lines = Strip-Ansi $raw; $text = ($lines -join "`n")
      $passed = $failed = $skipped = 0
      if ($text -match '(\d+)\s+passed')  { $passed  = [int]$Matches[1] }
      if ($text -match '(\d+)\s+failed')  { $failed  = [int]$Matches[1] }
      if ($text -match '(\d+)\s+skipped') { $skipped = [int]$Matches[1] }
      $total = $passed + $failed + $skipped
      # Playwright list reporter prints failures as "  N) [chromium] › file › title".
      $names = @($lines | ForEach-Object { if ($_ -match '^\s*\d+\)\s+(\[.+)$') { ($Matches[1]).Trim() } })
      $results['Playwright E2E'] = New-Result -Status ($ok ? 'PASS' : 'FAIL') -Passed $passed -Failed $failed -Skipped $skipped -Total $total -FailedNames $names
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
$grandPass = $grandFail = $grandSkip = 0
foreach ($k in $results.Keys) {
  $r = $results[$k]
  switch ($r.Status) {
    'SKIPPED' { Write-Host ("  {0,-16} SKIPPED" -f $k) -ForegroundColor Yellow }
    'PASS'    {
      $d = if ($r.Detail) { $r.Detail } else { "{0}/{1} passed" -f $r.Passed, $r.Total }
      Write-Host ("  {0,-16} PASS    {1}" -f $k, $d) -ForegroundColor Green
    }
    'FAIL'    {
      $anyFail = $true
      $d = if ($r.Detail) { $r.Detail } else { "{0}/{1} passed, {2} failed" -f $r.Passed, $r.Total, $r.Failed }
      Write-Host ("  {0,-16} FAIL    {1}" -f $k, $d) -ForegroundColor Red
      foreach ($n in $r.FailedNames) { Write-Host ("                     ✗ {0}" -f $n) -ForegroundColor Red }
      if ($r.Failed -gt 0 -and $r.FailedNames.Count -eq 0) {
        Write-Host "                     (failing test names not parsed — see output above)" -ForegroundColor DarkYellow
      }
    }
  }
  $grandPass += $r.Passed; $grandFail += $r.Failed; $grandSkip += $r.Skipped
}
Write-Host "  ----------------------------------------------" -ForegroundColor Cyan
Write-Host ("  Total: {0} passed, {1} failed, {2} skipped" -f $grandPass, $grandFail, $grandSkip) `
  -ForegroundColor ($(if ($anyFail) { 'Red' } else { 'Green' }))
Write-Host "==============================================" -ForegroundColor Cyan

[Console]::OutputEncoding = $prevOutputEncoding
if ($anyFail) { exit 1 } else { exit 0 }
