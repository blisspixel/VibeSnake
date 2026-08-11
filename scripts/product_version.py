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
        source = path.read_bytes().decode("utf-8", errors="strict")
    except (OSError, UnicodeError) as error:
        raise ValueError(f"Could not read canonical product version from {path}: {error}") from error
    if not source.endswith("\n") or source.count("\n") != 1 or "\r" in source:
        raise ValueError("VERSION must contain exactly one UTF-8 line terminated by LF")
    version = source[:-1]
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
