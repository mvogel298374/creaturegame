#requires -Version 7
# Prints the working diff's C# files and their changed line ranges as JSON; ranges is "all" for an untracked file.
Push-Location (git rev-parse --show-toplevel)
try {
  $files = [ordered]@{}
  $current = $null
  foreach ($line in git diff -U0 --diff-filter=d HEAD -- '*.cs') {
    if ($line -match '^\+\+\+ b/(.+)$') {
      $current = $Matches[1]
      $files[$current] = [System.Collections.Generic.List[object]]::new()
    }
    elseif ($current -and $line -match '^@@ -\S+ \+(\d+)(?:,(\d+))? @@') {
      $start = [int]$Matches[1]
      $count = $Matches[2] ? [int]$Matches[2] : 1
      if ($count -gt 0) { $files[$current].Add(@($start, ($start + $count - 1))) }
    }
  }
  foreach ($file in git ls-files --others --exclude-standard -- '*.cs') { $files[$file] = 'all' }

  $items = @(
    foreach ($file in $files.Keys) {
      if ($file -notmatch '/DB/Migrations/') { [pscustomobject]@{ file = $file; ranges = $files[$file] } }
    }
  )
  ConvertTo-Json -InputObject $items -Depth 4 -Compress
}
finally { Pop-Location }
