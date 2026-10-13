# Environment practice

Lessons Claude derived from working in this environment.

1. **Pick the shell before writing the command.** The PowerShell tool is pwsh 7 and the Bash tool is Git
   Bash; never mix their syntax. A multi-line commit message uses `@'…'@` (closing `'@` at column 0) in
   PowerShell and `git commit -F - <<'EOF'` in Bash. The repo scripts need pwsh 7, and `dotnet` means the SDK
   in the user folder, not the system runtime. A failed commit is said plainly before retrying.
2. **Stop the dev stack with `.\stop-dev.ps1`.** Never kill processes by name: killing every `node` also
   kills the MCP servers. If another program holds a dev port, leave it alone and tell Mo which PID holds it.
