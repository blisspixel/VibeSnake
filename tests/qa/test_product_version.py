"""Contracts for canonical native and Python package version identity."""

from __future__ import annotations

from pathlib import Path

import pytest

from scripts.product_version import package_version, read_product_version


@pytest.mark.parametrize(
    ("product", "package"),
    (
        ("0.3.0-alpha.1", "0.3.0a1"),
        ("1.2.3-beta.4", "1.2.3b4"),
        ("2.0.0-rc.5", "2.0.0rc5"),
        ("1.0.0", "1.0.0"),
    ),
)
def test_semver_maps_to_canonical_package_version(product: str, package: str) -> None:
    assert package_version(product) == package


@pytest.mark.parametrize(
    "version",
    ("01.0.0", "1.0", "1.0.0-alpha.0", "1.0.0-preview.1", "1.0.0+local", "../1.0.0"),
)
def test_noncanonical_product_versions_are_rejected(version: str) -> None:
    with pytest.raises(ValueError, match="Unsupported canonical product version"):
        package_version(version)


def test_version_file_must_contain_one_canonical_semver(tmp_path: Path) -> None:
    (tmp_path / "VERSION").write_text("0.3.0a1\n", encoding="utf-8")

    with pytest.raises(ValueError, match="VERSION must contain"):
        read_product_version(tmp_path)
