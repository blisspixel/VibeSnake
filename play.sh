#!/usr/bin/env bash
# Build and launch the native Godot game from a cloned checkout.
# Usage: ./play.sh

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v pwsh >/dev/null 2>&1; then
  cat >&2 <<'HELP'
Vibe Snake requires PowerShell 7 and the exact .NET 10.0.303 SDK.
Linux/macOS with mise:
  mise use -g powershell@7.6.6 dotnet@10.0.303
macOS with Homebrew:
  brew install --cask powershell
Install the pinned SDK with mise or the official dotnet-install script:
  https://dotnet.microsoft.com/download/dotnet/scripts
  bash dotnet-install.sh --version 10.0.303 --install-dir .dotnet
Then run ./play.sh again. See docs/guides/DEVELOPMENT.md.
HELP
  exit 1
fi

exec pwsh -NoProfile -File "${ROOT}/play.ps1" "$@"
