"""Fail if ProductIdentity.AppVersion drifts from pyproject package version."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PYPROJECT = ROOT / "pyproject.toml"
PRODUCT_IDENTITY = ROOT / "game" / "scripts" / "ProductIdentity.cs"


def _read_pyproject_version() -> str:
    text = PYPROJECT.read_text(encoding="utf-8")
    match = re.search(r'(?m)^version\s*=\s*"([^"]+)"\s*$', text)
    if match is None:
        raise SystemExit("Could not parse package version from pyproject.toml")
    return match.group(1)


def _read_product_identity_version() -> str:
    text = PRODUCT_IDENTITY.read_text(encoding="utf-8")
    match = re.search(
        r'public const string AppVersion = "([^"]+)";',
        text,
    )
    if match is None:
        raise SystemExit("Could not parse ProductIdentity.AppVersion")
    return match.group(1)


def main() -> int:
    package_version = _read_pyproject_version()
    product_version = _read_product_identity_version()
    if package_version != product_version:
        print(
            "Product version mismatch: "
            f"pyproject.toml={package_version!r} "
            f"ProductIdentity.AppVersion={product_version!r}",
            file=sys.stderr,
        )
        return 1
    print(f"Product versions aligned: {package_version}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
