#Requires -Version 7
<#
  Cleanly stop the dev stack that dev.ps1 starts.

  dev.ps1 launches two `Start-Process pwsh -NoExit` wrapper windows — one hosting `dotnet watch run`
  (backend, :5100) and one hosting `npm run dev` -> vite (frontend, :5173). Because of -NoExit, killing
  only the servers leaves those wrapper shells (and dotnet-watch's child processes) orphaned, and they
  pile up over repeated restarts. This stops the WHOLE tree.

  Scoped precisely to THIS repo, so it never touches unrelated processes — MCP-server node processes,
  other shells, the shared Roslyn/VBCSCompiler build server, or the caller's own PowerShell session.

  Detection = two independent signals, unioned:
    1. Whatever is listening on the dev ports (5100/5173) — the actual servers.
    2. The wrapper pwsh windows: pwsh started with -NoExit whose command line references this repo root.
  Each root is then expanded to ALL its descendants (npm/vite reparent through non-pwsh shims, so the
  walk is over the full process table, not name-filtered). The caller shell and its ancestors are
  protected so the script can never kill the session it runs in.

  Returns $true if anything was running (and stopped), $false if the stack was already clear.
#>
param([switch]$Quiet)

$root  = $PSScriptRoot
$ports = 5100, 5173

function Write-Info($msg) { if (-not $Quiet) { Write-Host $msg } }

# One snapshot of the process table for all parent/child + command-line math.
$all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine

# Protect the caller: never kill this shell or any of its ancestors (the harness/terminal chain).
$protected = [System.Collections.Generic.HashSet[int]]::new()
$cur = $PID
while ($cur -and $protected.Add([int]$cur)) {
    $cur = ($all | Where-Object ProcessId -eq $cur).ParentProcessId
}

# Roots to tear down: (1) port listeners, (2) repo-scoped -NoExit wrapper shells.
$roots = [System.Collections.Generic.HashSet[int]]::new()
foreach ($port in $ports) {
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { [void]$roots.Add([int]$_.OwningProcess) }
}
$rootEsc = [regex]::Escape($root)
$all | Where-Object {
    $_.Name -eq 'pwsh.exe' -and $_.CommandLine -and
    $_.CommandLine -match '-NoExit' -and $_.CommandLine -match $rootEsc
} | ForEach-Object { [void]$roots.Add([int]$_.ProcessId) }

# Index children by parent so each root expands to its whole subtree.
$childrenOf = @{}
foreach ($p in $all) {
    $ppid = [int]$p.ParentProcessId
    if (-not $childrenOf.ContainsKey($ppid)) { $childrenOf[$ppid] = @() }
    $childrenOf[$ppid] += [int]$p.ProcessId
}

# BFS from the roots downward (parents discovered before children -> a `dotnet watch` parent is killed
# before its app child, so it can't respawn the child mid-teardown). Skip anything protected.
$order   = [System.Collections.Generic.List[int]]::new()
$seen    = [System.Collections.Generic.HashSet[int]]::new()
$queue   = [System.Collections.Generic.Queue[int]]::new()
foreach ($r in $roots) { $queue.Enqueue($r) }
while ($queue.Count) {
    $id = $queue.Dequeue()
    if ($protected.Contains($id) -or -not $seen.Add($id)) { continue }
    $order.Add($id)
    if ($childrenOf.ContainsKey($id)) { foreach ($c in $childrenOf[$id]) { $queue.Enqueue($c) } }
}

if ($order.Count -eq 0) {
    Write-Info "Dev stack: nothing running (ports $($ports -join '/') already clear)."
    return $false
}

foreach ($id in $order) {
    $p = $all | Where-Object ProcessId -eq $id
    Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    Write-Info ("  killed PID {0,-6} {1}" -f $id, ($p.Name ?? '?'))
}

# Verify + retry: `dotnet watch` can respawn the app in the split second before it dies, so re-scan the
# ports a few times and kill any straggler listener + its subtree.
for ($try = 0; $try -lt 3; $try++) {
    Start-Sleep -Milliseconds 300
    $stragglers = @()
    foreach ($port in $ports) {
        $stragglers += Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess
    }
    $stragglers = $stragglers | Sort-Object -Unique | Where-Object { -not $protected.Contains([int]$_) }
    if (-not $stragglers) { break }
    foreach ($id in $stragglers) {
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        Write-Info ("  killed straggler PID {0}" -f $id)
    }
}

Write-Info "Dev stack stopped."
return $true
