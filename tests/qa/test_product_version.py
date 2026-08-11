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
    (tmp_path / "VERSION").write_bytes(b"0.3.0a1\n")

    with pytest.raises(ValueError, match="VERSION must contain"):
        read_product_version(tmp_path)


@pytest.mark.parametrize(
    "source",
    (
        " 0.3.0-alpha.1\n",
        "0.3.0-alpha.1 \n",
        "0.3.0-alpha.1\n\n",
        "0.3.0-alpha.1\r\n",
        "0.3.0-alpha.1",
    ),
)
def test_version_file_rejects_noncanonical_line_encoding(tmp_path: Path, source: str) -> None:
    (tmp_path / "VERSION").write_bytes(source.encode("utf-8"))

    with pytest.raises(ValueError, match="VERSION must contain"):
        read_product_version(tmp_path)


def test_version_file_accepts_exactly_one_lf_terminated_line(tmp_path: Path) -> None:
    (tmp_path / "VERSION").write_bytes(b"0.3.0-alpha.1\n")

    assert read_product_version(tmp_path) == "0.3.0-alpha.1"
