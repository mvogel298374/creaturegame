#requires -Version 7
<#
.SYNOPSIS
  Runs the Playwright E2E suite (creaturegame.Web/ClientApp/e2e) with readable,
  timing-aware output.

.DESCRIPTION
  The E2E suite is slow by construction: `playwright.config.ts` pins workers: 1 /
  fullyParallel: false (battles are stateful — one in-flight battle per SignalR
  connection), and several specs walk whole runs seed-by-seed to reach a deep state.
  So the useful levers are (a) run fewer specs and (b) see where the time actually goes.

  This script gives you both:
    • -Spec / -Grep / -LastFailed / -Bail  → run a subset instead of all of them
    • a JSON report parsed into a per-file + per-test timing summary, the slowest
      tests, and full failure detail with the trace/video/screenshot paths

  Playwright's own live `list` output still streams through unchanged (colours intact)
  while the run is in progress — the summary is printed after it.

  The .NET backend (:5100) must be up: Playwright's webServer block starts/reuses Vite
  (:5173) but never the backend. Pass -StartStack to have this script start it for the
  run and stop it afterwards.

.PARAMETER Spec
  One or more filename filters, matched against the spec path (Playwright's positional
  filter, a regex). 'battle' runs battle.spec.ts AND battle-ui-cues.spec.ts; use
  'battle.spec' to pin one. Tab-completes from e2e/*.spec.ts.

.PARAMETER Grep
  Only run tests whose title matches this regex (playwright -g).

.PARAMETER GrepInvert
  Skip tests whose title matches this regex (playwright --grep-invert).

.PARAMETER Workers
  Override the config's single worker. The suite is serial on purpose — raising this
  is an experiment, not a supported mode, and stateful-battle specs may cross-talk.

.PARAMETER Retries
  Retry failing tests N times (config default is 0). A test that passes on retry is
  reported as FLAKY.

.PARAMETER Bail
  Stop the whole run at the first failure (--max-failures=1). The big time-saver when
  you are iterating on one broken spec.

.PARAMETER Headed
  Run with a visible browser window.

.PARAMETER Ui
  Open Playwright's UI mode (watch/pick/inspect) instead of a headless run. Interactive:
  no summary is produced.

.PARAMETER Inspect
  Run under the Playwright Inspector (--debug). Implies headed, serial, no timeout.

.PARAMETER ListOnly
  List the tests that match the filters without running them.

.PARAMETER Html
  Additionally write the HTML report and open it at the end (`playwright show-report`).

.PARAMETER StartStack
  Start the .NET backend for the run if it isn't already reachable, and stop it after.

.PARAMETER Slowest
  How many slowest tests to list in the summary (default 5, 0 disables).

.EXAMPLE
  .\e2e.ps1                              # whole suite against a running stack
.EXAMPLE
  .\e2e.ps1 -StartStack                  # ...starting/stopping the backend itself
.EXAMPLE
  .\e2e.ps1 -Spec battle.spec -Headed    # one file, visible browser
.EXAMPLE
  .\e2e.ps1 -Grep "cadence" -Bail        # by title, stop on first failure
.EXAMPLE
  .\e2e.ps1 -LastFailed                  # re-run only what failed last time
#>
[CmdletBinding()]
param(
  [ArgumentCompleter({
    param($cmd, $param, $wordToComplete)
    $dir = Join-Path $PSScriptRoot 'creaturegame.Web\ClientApp\e2e'
    Get-ChildItem -Path $dir -Filter '*.spec.ts' -ErrorAction SilentlyContinue |
      ForEach-Object { $_.Name -replace '\.spec\.ts$', '' } |
      Where-Object { $_ -like "$wordToComplete*" }
  })]
  [string[]]$Spec,

  [string]$Grep,
  [string]$GrepInvert,
  [int]$Workers,
  [int]$Retries = -1,
  [switch]$Bail,
  [switch]$Headed,
  [switch]$Ui,
  [switch]$Inspect,
  [switch]$ListOnly,
  [switch]$Html,
  [switch]$LastFailed,
  [switch]$StartStack,
  [int]$Slowest = 5
)

$ErrorActionPreference = 'Stop'

$root      = $PSScriptRoot
$clientApp = Join-Path $root 'creaturegame.Web\ClientApp'
$dotnetExe = if ($env:DOTNET_EXE) { $env:DOTNET_EXE }
             elseif (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" }
             else { 'dotnet' }

# ── helpers ──────────────────────────────────────────────────────────────────
function Test-Backend {
  try { return (Invoke-WebRequest 'http://localhost:5100/api/Species' -UseBasicParsing -TimeoutSec 2).StatusCode -ge 200 }
  catch { return $false }
}

# Invariant culture on purpose: a locale that renders "4,9s" for 4.9 seconds reads as a thousands
# separator to anyone scanning a timing column.
function Format-Duration([double]$ms) {
  $inv = [cultureinfo]::InvariantCulture
  if ($ms -lt 1000)  { return [string]::Format($inv, '{0,5:N0}ms', $ms) }
  if ($ms -lt 60000) { return [string]::Format($inv, '{0,6:N1}s',  ($ms / 1000)) }
  return [string]::Format($inv, '{0,3:N0}m{1:N0}s', [math]::Floor($ms / 60000), (($ms % 60000) / 1000))
}

# Playwright colourises error messages even into a JSON payload.
function Strip-Ansi([string]$s) { ($s ?? '') -replace "`e\[[0-9;]*[a-zA-Z]", '' }

# The JSON report omits empty collections entirely (a spec file with no describe block has no
# `suites` property at all), and `@($null)` is a ONE-element array holding $null — which would
# walk the tree into a null node. Always go through this.
function AsList($x) { if ($null -eq $x) { @() } else { @($x) | Where-Object { $null -ne $_ } } }

function Write-Rule([string]$text, [string]$colour = 'Cyan') {
  Write-Host ("── {0} " -f $text).PadRight(78, '─') -ForegroundColor $colour
}

# ── build the playwright argument list ───────────────────────────────────────
$pwArgs = @('playwright', 'test')
if ($Ui)          { $pwArgs += '--ui' }
if ($Inspect)     { $pwArgs += '--debug' }
if ($ListOnly)    { $pwArgs += '--list' }
if ($Headed)      { $pwArgs += '--headed' }
if ($LastFailed)  { $pwArgs += '--last-failed' }
if ($Bail)        { $pwArgs += '--max-failures=1' }
if ($Workers -gt 0) { $pwArgs += "--workers=$Workers" }
if ($Retries -ge 0) { $pwArgs += "--retries=$Retries" }
if ($Grep)        { $pwArgs += @('-g', $Grep) }
if ($GrepInvert)  { $pwArgs += @('--grep-invert', $GrepInvert) }
if ($Spec)        { $pwArgs += $Spec }

# Interactive/inspection modes own the terminal — no JSON report, no summary.
$interactive = $Ui -or $Inspect -or $ListOnly

$jsonPath = $null
if (-not $interactive) {
  $reporters = if ($Html) { 'list,json,html' } else { 'list,json' }
  $pwArgs += "--reporter=$reporters"
  $scratch = if ($env:TEMP) { $env:TEMP } else { $root }
  $jsonPath = Join-Path $scratch ("cg-e2e-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
  $env:PLAYWRIGHT_JSON_OUTPUT_NAME = $jsonPath
  # The html reporter otherwise blocks at the end serving the report; we open it ourselves.
  $env:PLAYWRIGHT_HTML_OPEN = 'never'
}

# ── backend ──────────────────────────────────────────────────────────────────
$startedBackend = $false
$backend = $null
$exitCode = 1

try {
  # -ListOnly never launches a browser or the webServer, so it needs no stack.
  if (-not $ListOnly -and -not (Test-Backend)) {
    if ($StartStack) {
      Write-Host "Starting backend on :5100 for the E2E run..." -ForegroundColor DarkCyan
      $backend = Start-Process $dotnetExe -ArgumentList 'run', '--project', (Join-Path $root 'creaturegame.Web') -PassThru -WindowStyle Hidden
      $startedBackend = $true
      $deadline = (Get-Date).AddSeconds(90)
      while (-not (Test-Backend) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
      if (-not (Test-Backend)) { throw "Backend did not come up on :5100 within 90s." }
      Write-Host "Backend is up." -ForegroundColor DarkCyan
    }
    else {
      Write-Host ""
      Write-Host "  Backend not reachable on http://localhost:5100." -ForegroundColor Red
      Write-Host "  Start the stack with .\dev.ps1, or re-run with -StartStack." -ForegroundColor Yellow
      Write-Host ""
      exit 2
    }
  }

  # ── run ────────────────────────────────────────────────────────────────────
  $what = if ($Spec) { $Spec -join ', ' } else { 'all specs' }
  Write-Host ""
  Write-Rule "E2E (Playwright) — $what"
  if ($Grep)          { Write-Host ("   title filter : {0}" -f $Grep) -ForegroundColor DarkGray }
  if ($GrepInvert)    { Write-Host ("   skipping     : {0}" -f $GrepInvert) -ForegroundColor DarkGray }
  if ($Workers -gt 0) { Write-Host ("   workers      : {0}  (config default is 1 — the suite is serial on purpose)" -f $Workers) -ForegroundColor DarkYellow }
  if ($Bail)          { Write-Host  "   bail         : stopping at the first failure" -ForegroundColor DarkGray }
  Write-Host ""

  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  Push-Location $clientApp
  try { & npx @pwArgs } finally { Pop-Location }
  $exitCode = $LASTEXITCODE
  $sw.Stop()

  if ($interactive) { exit $exitCode }

  # ── parse the JSON report ──────────────────────────────────────────────────
  if (-not (Test-Path $jsonPath)) {
    Write-Host ""
    Write-Host "  No JSON report was written — Playwright likely failed before running any test." -ForegroundColor Yellow
    Write-Host "  See the output above. (Wall clock: {0})" -f (Format-Duration $sw.Elapsed.TotalMilliseconds) -ForegroundColor Yellow
    exit $exitCode
  }

  $report = Get-Content -Raw -Path $jsonPath | ConvertFrom-Json

  # Flatten the suite tree (file suite → describe suites → specs → tests → results).
  $flat = [System.Collections.Generic.List[object]]::new()
  function Read-Suite($suite, [string]$file, [string[]]$titlePath) {
    $f = if ($suite.PSObject.Properties['file'] -and $suite.file) { $suite.file } else { $file }
    # [string[]] on purpose: assigning a one-element array through an if-expression UNWRAPS it to a
    # bare string, and `$path + $title` would then be string concatenation, not an array append —
    # silently gluing "Endless chain" and "a run ends…" into one word.
    [string[]]$path = @($titlePath)
    foreach ($spec in (AsList $suite.specs)) {
      foreach ($test in (AsList $spec.tests)) {
        $results  = AsList $test.results
        $last     = if ($results.Count) { $results[-1] } else { $null }
        $duration = ($results | Measure-Object -Property duration -Sum).Sum
        $errText  = @()
        foreach ($r in $results) {
          if ($r.PSObject.Properties['error'] -and $r.error -and $r.error.message) {
            $errText += (Strip-Ansi $r.error.message)
          }
        }
        $attachments = @()
        if ($last -and $last.PSObject.Properties['attachments']) {
          foreach ($a in (AsList $last.attachments)) { if ($a.path) { $attachments += "$($a.name): $($a.path)" } }
        }
        $flat.Add([pscustomobject]@{
          File        = $f
          Title       = ((@($path) + $spec.title) -join ' › ')
          Status      = $test.status          # expected | unexpected | flaky | skipped
          Duration    = [double]$duration
          Retries     = [math]::Max(0, $results.Count - 1)
          Errors      = $errText
          Attachments = $attachments
        })
      }
    }
    foreach ($child in (AsList $suite.suites)) { Read-Suite $child $f (@($path) + $child.title) }
  }
  foreach ($s in (AsList $report.suites)) { Read-Suite $s $s.title @() }

  # ── summary ────────────────────────────────────────────────────────────────
  $passed  = @($flat | Where-Object Status -eq 'expected')
  $failed  = @($flat | Where-Object Status -eq 'unexpected')
  $flaky   = @($flat | Where-Object Status -eq 'flaky')
  $skipped = @($flat | Where-Object Status -eq 'skipped')
  $ran     = $flat.Count - $skipped.Count

  # A typo'd -Spec/-Grep matches nothing; Playwright exits non-zero but there is no tally to print,
  # and "PASS — 0 passed" would be a lie.
  if ($flat.Count -eq 0) {
    Write-Host ""
    Write-Host "  No tests matched the filters." -ForegroundColor Yellow
    if ($Spec) { Write-Host ("    -Spec  {0}" -f ($Spec -join ', ')) -ForegroundColor Yellow }
    if ($Grep) { Write-Host ("    -Grep  {0}" -f $Grep) -ForegroundColor Yellow }
    Write-Host "  Run .\e2e.ps1 -ListOnly to see the available tests." -ForegroundColor DarkGray
    Write-Host ""
    exit ($exitCode -eq 0 ? 1 : $exitCode)
  }

  Write-Host ""
  Write-Rule 'E2E SUMMARY'

  # Per-file: counts + total time, slowest file first — this is the "where does the time go" view.
  $byFile = $flat | Group-Object File | Sort-Object { -($_.Group | Measure-Object Duration -Sum).Sum }
  foreach ($g in $byFile) {
    $fFail = @($g.Group | Where-Object Status -eq 'unexpected').Count
    $fFlak = @($g.Group | Where-Object Status -eq 'flaky').Count
    $fSkip = @($g.Group | Where-Object Status -eq 'skipped').Count
    $fPass = @($g.Group | Where-Object Status -eq 'expected').Count
    $time  = Format-Duration (($g.Group | Measure-Object Duration -Sum).Sum)

    $mark, $colour =
      if ($fFail) { '✗', 'Red' }
      elseif ($fFlak) { '!', 'DarkYellow' }
      elseif ($fPass -eq 0) { '-', 'DarkGray' }
      else { '✓', 'Green' }

    $counts = "$fPass passed"
    if ($fFail) { $counts += ", $fFail failed" }
    if ($fFlak) { $counts += ", $fFlak flaky" }
    if ($fSkip) { $counts += ", $fSkip skipped" }

    Write-Host ("  {0} {1,-28} {2}  {3}" -f $mark, ($g.Name -replace '\.spec\.ts$', ''), $time, $counts) -ForegroundColor $colour
  }

  # Slowest tests — the actionable part when the suite "takes a long time". Pointless for a run
  # small enough that the per-file list above already says it.
  if ($Slowest -gt 0 -and $ran -gt 3) {
    Write-Host ""
    Write-Host ("  Slowest {0} tests" -f [math]::Min($Slowest, $ran)) -ForegroundColor DarkCyan
    $flat | Where-Object Status -ne 'skipped' | Sort-Object Duration -Descending | Select-Object -First $Slowest |
      ForEach-Object {
        Write-Host ("    {0}  {1} › {2}" -f (Format-Duration $_.Duration), ($_.File -replace '\.spec\.ts$', ''), $_.Title) -ForegroundColor DarkGray
      }
  }

  # Failures in full, with the artefacts to open next.
  if ($failed.Count -or $flaky.Count) {
    Write-Host ""
    Write-Rule 'FAILURES' 'Red'
    foreach ($t in @($failed) + @($flaky)) {
      $tag = if ($t.Status -eq 'flaky') { "FLAKY (passed on retry $($t.Retries))" } else { 'FAILED' }
      Write-Host ("  ✗ {0} › {1}   [{2}]" -f ($t.File -replace '\.spec\.ts$', ''), $t.Title, $tag) -ForegroundColor Red
      foreach ($line in (($t.Errors -join "`n") -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 6)) {
        Write-Host ("      {0}" -f $line.TrimEnd()) -ForegroundColor DarkRed
      }
      foreach ($a in $t.Attachments) { Write-Host ("      · {0}" -f $a) -ForegroundColor DarkGray }
      Write-Host ""
    }
    Write-Host "  Open a trace with:  npx playwright show-trace <trace.zip path above>" -ForegroundColor DarkGray
  }

  # Bottom line.
  $wall  = Format-Duration $sw.Elapsed.TotalMilliseconds
  $tally = "{0} passed" -f $passed.Count
  if ($failed.Count)  { $tally += ", {0} failed"  -f $failed.Count }
  if ($flaky.Count)   { $tally += ", {0} flaky"   -f $flaky.Count }
  if ($skipped.Count) { $tally += ", {0} skipped" -f $skipped.Count }

  Write-Host ""
  Write-Host ('─' * 78) -ForegroundColor Cyan
  $verdict = if ($failed.Count) { 'FAIL' } elseif ($flaky.Count) { 'PASS (with flakes)' } else { 'PASS' }
  Write-Host ("  {0}   {1}   in {2}" -f $verdict, $tally, $wall) `
    -ForegroundColor ($(if ($failed.Count) { 'Red' } elseif ($flaky.Count) { 'DarkYellow' } else { 'Green' }))
  Write-Host ('─' * 78) -ForegroundColor Cyan

  if ($Html) {
    Write-Host "  Opening the HTML report..." -ForegroundColor DarkGray
    Push-Location $clientApp
    try { Start-Process npx -ArgumentList 'playwright', 'show-report' } finally { Pop-Location }
  }
}
finally {
  if ($startedBackend -and $backend -and -not $backend.HasExited) {
    Write-Host "Stopping the backend started for this run..." -ForegroundColor DarkCyan
    Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue
  }
  if ($jsonPath -and (Test-Path $jsonPath)) { Remove-Item $jsonPath -ErrorAction SilentlyContinue }
  Remove-Item Env:\PLAYWRIGHT_JSON_OUTPUT_NAME -ErrorAction SilentlyContinue
  Remove-Item Env:\PLAYWRIGHT_HTML_OPEN -ErrorAction SilentlyContinue
}

exit $exitCode
