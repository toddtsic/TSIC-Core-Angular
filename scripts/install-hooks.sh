#!/bin/sh
set -e
REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$REPO_ROOT"
if [ ! -d ".githooks" ]; then
  echo ".githooks directory not found in $REPO_ROOT"
  exit 1
fi
git config core.hooksPath .githooks
echo "Configured git to use .githooks (core.hooksPath). To undo: git config --unset core.hooksPath"
