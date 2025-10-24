#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR/.."
COMPOSE_FILE="$REPO_ROOT/tools/aspire/docker-compose.aspire.yml"

if [ ! -f "$COMPOSE_FILE" ]; then
  echo "Compose file not found: $COMPOSE_FILE" >&2
  exit 1
fi

echo "Running Aspire docker-compose ($COMPOSE_FILE) ..."
if command -v docker >/dev/null 2>&1; then
  docker compose -f "$COMPOSE_FILE" up --build
else
  echo "Docker CLI not found in PATH. Install Docker Desktop or Docker CLI to run Aspire compose." >&2
  exit 1
fi
