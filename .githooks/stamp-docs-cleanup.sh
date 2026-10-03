#!/bin/sh
# Written by the docs-cleanup agent as its LAST action (CLAUDE.md > Process gates, Gate 2). The pre-commit hook
# blocks a commit with no stamp, or with any staged file modified after it. Nobody else creates this file.

GIT_DIR=$(git rev-parse --git-dir)
{
  echo "docs-cleanup completed $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  git diff --cached --name-only
} > "$GIT_DIR/docs-cleanup.stamp"
echo "[docs-cleanup] stamp written: $GIT_DIR/docs-cleanup.stamp"
