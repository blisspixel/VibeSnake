"""Canonical Vibe Snake product-version parsing and package mapping."""

from __future__ import annotations

import re
from pathlib import Path


SEMVER_PATTERN = re.compile(
    r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    r"(?:-(alpha|beta|rc)\.([1-9][0-9]*))?"
)


def read_product_version(root: Path) -> str:
    """Read and validate the repository's canonical SemVer product version."""
    path = root / "VERSION"
    try:
        version = path.read_text(encoding="utf-8").strip()
    except OSError as error:
        raise ValueError(f"Could not read canonical product version from {path}: {error}") from error
    if not SEMVER_PATTERN.fullmatch(version):
        raise ValueError(f"VERSION must contain one canonical stable or prerelease SemVer; got {version!r}")
    return version


def package_version(product_version: str) -> str:
    """Map canonical SemVer to the equivalent canonical PEP 440 version."""
    match = SEMVER_PATTERN.fullmatch(product_version)
    if match is None:
        raise ValueError(f"Unsupported canonical product version: {product_version!r}")
    major, minor, patch, prerelease, prerelease_number = match.groups()
    stable = f"{major}.{minor}.{patch}"
    if prerelease is None:
        return stable
    marker = {"alpha": "a", "beta": "b", "rc": "rc"}[prerelease]
    return f"{stable}{marker}{prerelease_number}"
