#!/usr/bin/env bash
# Prepare a source checkout and launch the native Godot game.
# The frozen Python reference is retired; this script no longer installs it.
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/blisspixel/VibeSnake/main/scripts/install_player.sh | bash
#   or: ./scripts/install_player.sh [install_dir]

set -euo pipefail

INSTALL_DIR="${1:-$PWD/VibeSnake}"
BRANCH="${VIBESNAKE_BRANCH:-main}"
REPO_URL="${VIBESNAKE_REPO:-https://github.com/blisspixel/VibeSnake.git}"

echo "The Python/Pygame reference is retired. Preparing the native Godot game in ${INSTALL_DIR} (branch ${BRANCH})."

if [[ ! -d "${INSTALL_DIR}/.git" ]]; then
  git clone --branch "${BRANCH}" "${REPO_URL}" "${INSTALL_DIR}"
else
  echo "Directory exists; pulling latest ${BRANCH}"
  git -C "${INSTALL_DIR}" fetch origin "${BRANCH}"
  git -C "${INSTALL_DIR}" checkout "${BRANCH}"
  git -C "${INSTALL_DIR}" pull --ff-only origin "${BRANCH}"
fi

echo "Launching ./play.sh. It checks for PowerShell 7 and the .NET SDK 10.0.303, then builds Release."
exec "${INSTALL_DIR}/play.sh"
