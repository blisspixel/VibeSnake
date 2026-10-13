#!/usr/bin/env bash
# Build and launch the native Godot game from a cloned checkout.
# Usage: ./play.sh
#        ./play.sh --install-desktop-entry

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

install_desktop_entry() {
  local applications="${XDG_DATA_HOME:-${HOME}/.local/share}/applications"
  local icon="${ROOT}/assets/images/logo.png"
  mkdir -p "${applications}"
  cat > "${applications}/vibesnake.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Vibe Snake
Comment=Launch the native Vibe Snake game
Exec=${ROOT}/play.sh
Icon=${icon}
Terminal=false
Categories=Game;
EOF
  echo "Installed ${applications}/vibesnake.desktop"
}

if [[ "${1:-}" == "--install-desktop-entry" ]]; then
  install_desktop_entry
  exit 0
fi

missing=0
if ! command -v pwsh >/dev/null 2>&1; then
  cat >&2 <<'EOF'
Vibe Snake requires PowerShell 7 or newer. Install pwsh, then run ./play.sh again.

  mise:      mise use -g powershell@7.6.6
  Homebrew:  brew install --cask powershell
  winget:    winget install --id Microsoft.PowerShell -e
EOF
  missing=1
fi

sdk_present=0
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.0\.303'; then
  sdk_present=1
fi
if [[ "${sdk_present}" -ne 1 ]]; then
  cat >&2 <<'EOF'
Vibe Snake requires exactly the .NET SDK 10.0.303 (global.json sets rollForward to disable).

  mise:      mise use -g dotnet@10.0.303
  Homebrew:  brew install dotnet@10
  winget:    winget install --id Microsoft.DotNet.SDK.10 -e
  script:    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 10.0.303
             export DOTNET_ROOT="$HOME/.dotnet"
             export PATH="$HOME/.dotnet:$PATH"
EOF
  missing=1
fi

if [[ "${missing}" -ne 0 ]]; then
  exit 1
fi

exec pwsh -NoProfile -File "${ROOT}/play.ps1" "$@"
