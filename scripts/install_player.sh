#!/usr/bin/env bash
# Bootstrap the native source launcher from GitHub main.
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/blisspixel/VibeSnake/main/scripts/install_player.sh | bash
#   or: ./scripts/install_player.sh [install_dir]

set -euo pipefail

INSTALL_DIR="${1:-$PWD/VibeSnake}"
BRANCH="${VIBESNAKE_BRANCH:-main}"
REPO_URL="${VIBESNAKE_REPO:-https://github.com/blisspixel/VibeSnake.git}"
echo "Installing the native Vibe Snake source into ${INSTALL_DIR} (branch ${BRANCH})"
if [[ ! -d "${INSTALL_DIR}" ]]; then
  git clone --branch "${BRANCH}" "${REPO_URL}" "${INSTALL_DIR}"
else
  if [[ "$(git -C "${INSTALL_DIR}" branch --show-current)" != "${BRANCH}" ]] ||
     [[ -n "$(git -C "${INSTALL_DIR}" status --porcelain)" ]]; then
    echo "Existing checkout has local work or a different branch. Run its play.sh, or choose a new install directory." >&2
    exit 1
  fi
  git -C "${INSTALL_DIR}" pull --ff-only origin "${BRANCH}"
fi
exec "${INSTALL_DIR}/play.sh"
