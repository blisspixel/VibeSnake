"""Fail if canonical, native, and Python package versions drift."""

from __future__ import annotations

import re
import sys
from pathlib import Path

try:
    from product_version import package_version, read_product_version
except ModuleNotFoundError:  # Imported as scripts.check_product_version in tests.
    from scripts.product_version import package_version, read_product_version

ROOT = Path(__file__).resolve().parents[1]
PYPROJECT = ROOT / "pyproject.toml"
PRODUCT_IDENTITY = ROOT / "game" / "scripts" / "ProductIdentity.cs"
PYTHON_IDENTITY = ROOT / "src" / "vibesnake" / "__init__.py"


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


def _read_python_fallback_version() -> str:
    text = PYTHON_IDENTITY.read_text(encoding="utf-8")
    match = re.search(r'__version__ = "([^"]+)"', text)
    if match is None:
        raise SystemExit("Could not parse Python fallback __version__")
    return match.group(1)


def main() -> int:
    try:
        canonical_version = read_product_version(ROOT)
        expected_package_version = package_version(canonical_version)
    except ValueError as error:
        print(error, file=sys.stderr)
        return 1
    package_value = _read_pyproject_version()
    product_version = _read_product_identity_version()
    python_fallback = _read_python_fallback_version()
    if (
        product_version != canonical_version
        or package_value != expected_package_version
        or python_fallback != expected_package_version
    ):
        print(
            "Product version mismatch: "
            f"VERSION={canonical_version!r} "
            f"pyproject.toml={package_value!r} "
            f"Python fallback={python_fallback!r} "
            f"ProductIdentity.AppVersion={product_version!r}; "
            f"expected package version={expected_package_version!r}",
            file=sys.stderr,
        )
        return 1
    print(f"Product versions aligned: product={canonical_version} package={expected_package_version}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
