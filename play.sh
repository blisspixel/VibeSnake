#!/usr/bin/env bash
# One-click play helper for a cloned Vibe Snake checkout.
# Usage: ./play.sh

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${ROOT}"

if [[ -x "${ROOT}/.venv/bin/python" ]]; then
  exec "${ROOT}/.venv/bin/python" -m vibesnake "$@"
fi

if command -v vibesnake >/dev/null 2>&1; then
  exec vibesnake "$@"
fi

echo "No virtual environment found."
echo "Run ./scripts/install_player.sh first, or:"
echo "  python3.14 -m venv .venv"
echo "  source .venv/bin/activate"
echo "  python -m pip install --require-hashes --only-binary=:all: -r requirements-runtime.lock"
echo "  python -m pip install --no-deps --no-build-isolation -e ."
echo "  ./play.sh"
exit 1
