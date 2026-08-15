#!/usr/bin/env bash
# Bootstrap the frozen Python reference from GitHub main.
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/blisspixel/VibeSnake/main/scripts/install_player.sh | bash
#   or: ./scripts/install_player.sh [install_dir]

set -euo pipefail

INSTALL_DIR="${1:-$PWD/VibeSnake}"
BRANCH="${VIBESNAKE_BRANCH:-main}"
REPO_URL="${VIBESNAKE_REPO:-https://github.com/blisspixel/VibeSnake.git}"
PYTHON_BIN="${VIBESNAKE_PYTHON:-python3.14}"

echo "Installing the frozen Vibe Snake Python reference into ${INSTALL_DIR} (branch ${BRANCH})"

if [[ ! -d "${INSTALL_DIR}" ]]; then
  git clone --branch "${BRANCH}" "${REPO_URL}" "${INSTALL_DIR}"
else
  echo "Directory exists; pulling latest ${BRANCH}"
  git -C "${INSTALL_DIR}" fetch origin "${BRANCH}"
  git -C "${INSTALL_DIR}" checkout "${BRANCH}"
  git -C "${INSTALL_DIR}" pull --ff-only origin "${BRANCH}"
fi

cd "${INSTALL_DIR}"
if [[ ! -d .venv ]]; then
  "${PYTHON_BIN}" -m venv .venv
fi

# shellcheck disable=SC1091
source .venv/bin/activate
python -m pip install --upgrade pip
python -m pip install --require-hashes --only-binary=:all: -r requirements-runtime.lock
python -m pip install --no-deps --no-build-isolation -e .

cat <<EOF

Frozen reference installed. Run it with:
  cd "${INSTALL_DIR}"
  source .venv/bin/activate
  vibesnake

Update later with:
  vibesnake update
EOF
