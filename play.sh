#!/usr/bin/env bash
# Build and launch the native Godot game from a cloned checkout.
# Usage: ./play.sh

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v pwsh >/dev/null 2>&1; then
  echo "Vibe Snake requires PowerShell 7 or newer. Install pwsh, then run ./play.sh again." >&2
  exit 1
fi

exec pwsh -NoProfile -File "${ROOT}/play.ps1" "$@"
