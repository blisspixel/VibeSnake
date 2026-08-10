"""Contracts for fail-closed unsigned native alpha assembly."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from scripts.assemble_unsigned_preview import EXTENSIONS, PLATFORMS, assemble_unsigned_preview


VERSION = "0.3.0-alpha.1"
REVISION = "a" * 40


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def _fixture(root: Path) -> tuple[Path, Path, Path, Path, Path]:
    version_root = root / "source"
    version_root.mkdir()
    (version_root / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
    channel_root = root / "channels"
    provenance_root = root / "provenance"
    rows = []
    for platform in PLATFORMS:
        artifact_root = channel_root / f"vibesnake-{platform}-unsigned-channel-shape"
        artifact_root.mkdir(parents=True)
        package_name = f"VibeSnake-{VERSION}-{platform}-qualification{EXTENSIONS[platform]}"
        package_path = artifact_root / package_name
        package_path.write_bytes((platform + " qualified player").encode())
        manifest_path = artifact_root / "artifact-manifest.json"
        _write_json(
            manifest_path,
            {
                "schemaVersion": 2,
                "product": "Vibe Snake",
                "platform": platform,
                "buildMode": "Release",
                "sourceRevision": REVISION,
            },
        )
        plan_path = artifact_root / "release_output_plan.json"
        _write_json(
            plan_path,
            {
                "schemaVersion": 1,
                "kind": "release-output-plan-v1",
                "product": "Vibe Snake",
                "productVersion": VERSION,
                "platform": platform,
                "directDownloadFileName": package_name,
                "passed": True,
                "qualificationOnly": True,
                "assemblyEligible": True,
                "publicationEligible": False,
                "optionalPackOutputSeparate": True,
                "baseGameIncludesOptionalPacks": False,
                "playerDataExcluded": True,
                "uninstallPreservesPlayerData": True,
                "deterministicRepeatMatched": True,
                "packageBytes": package_path.stat().st_size,
                "packageSha256": _sha256(package_path),
            },
        )
        (artifact_root / "SHA256SUMS").write_text(
            "".join(f"{_sha256(path)} *{path.name}\n" for path in (package_path, manifest_path, plan_path)),
            encoding="utf-8",
        )
        provenance_path = provenance_root / f"vibesnake-{platform}-provenance" / "attestation.jsonl"
        provenance_path.parent.mkdir(parents=True)
        provenance_path.write_text(f"provenance for {platform}\n", encoding="utf-8")
        rows.append(
            {
                "platform": platform,
                "artifactManifestSha256": _sha256(manifest_path),
                "packageSha256": _sha256(package_path),
                "packageBytes": package_path.stat().st_size,
                "directDownloadFileName": package_name,
            }
        )
    matrix_path = root / "matrix.json"
    _write_json(
        matrix_path,
        {
            "schemaVersion": 1,
            "kind": "release-matrix-qualification-v1",
            "passed": True,
            "sourceRevision": REVISION,
            "buildMode": "Release",
            "productVersion": VERSION,
            "publicationEligible": False,
            "platforms": rows,
        },
    )
    return channel_root, provenance_root, matrix_path, version_root, root / "preview"


def _assemble(root: Path, tag: str = f"v{VERSION}") -> tuple[list[str], dict[str, object]]:
    channel_root, provenance_root, matrix_path, version_root, output_root = _fixture(root)
    return assemble_unsigned_preview(
        channel_root,
        provenance_root,
        matrix_path,
        version_root,
        tag,
        REVISION,
        output_root,
    )


def test_complete_alpha_matrix_assembles_explicit_unsigned_preview(tmp_path: Path) -> None:
    errors, evidence = _assemble(tmp_path)

    assert errors == []
    assert evidence["passed"] is True
    assert evidence["unsigned"] is True
    assert evidence["stablePublicationEligible"] is False
    assert evidence["releaseMatrixSha256"] == _sha256(tmp_path / "matrix.json")
    assert len(evidence["packages"]) == 3
    output_root = tmp_path / "preview"
    assert (output_root / "unsigned_preview_manifest.json").is_file()
    checksums = (output_root / "SHA256SUMS.txt").read_text(encoding="utf-8")
    assert "-windows-x64-unsigned-preview.zip" in checksums
    assert "-macos-universal-unsigned-preview.zip" in checksums
    assert "-linux-x64-unsigned-preview.tar.gz" in checksums
    assert "qualification" not in checksums


def test_nonmatching_or_nonalpha_tag_is_rejected_without_output(tmp_path: Path) -> None:
    errors, evidence = _assemble(tmp_path, tag="v0.3.0")

    assert evidence["passed"] is False
    assert any("tag must exactly match" in error for error in errors)
    assert not (tmp_path / "preview").exists()


def test_stable_version_cannot_use_unsigned_preview_path(tmp_path: Path) -> None:
    channel_root, provenance_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    (version_root / "VERSION").write_text("0.3.0\n", encoding="utf-8")

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        matrix_path,
        version_root,
        "v0.3.0",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("requires a canonical alpha" in error for error in errors)
    assert not output_root.exists()


def test_tampered_package_and_unexpected_input_file_are_rejected(tmp_path: Path) -> None:
    channel_root, provenance_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    windows_root = channel_root / "vibesnake-windows-x64-unsigned-channel-shape"
    package_path = next(windows_root.glob("*qualification.zip"))
    package_path.write_bytes(b"tampered")
    (windows_root / "unexpected.txt").write_text("unexpected\n", encoding="utf-8")

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("byte count changed" in error for error in errors)
    assert any("unexpected file set" in error for error in errors)
    assert not output_root.exists()


def test_matrix_revision_and_provenance_must_match_complete_set(tmp_path: Path) -> None:
    channel_root, provenance_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    missing = provenance_root / "vibesnake-linux-x64-provenance" / "attestation.jsonl"
    missing.unlink()

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        "b" * 40,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("matrix.sourceRevision" in error for error in errors)
    assert any("linux-x64 provenance" in error for error in errors)
    assert not output_root.exists()


def test_missing_manifest_fails_cleanly_without_partial_output(tmp_path: Path) -> None:
    channel_root, provenance_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    manifest_path = channel_root / "vibesnake-macos-universal-unsigned-channel-shape" / "artifact-manifest.json"
    manifest_path.unlink()

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("missing macos-universal artifact manifest" in error for error in errors)
    assert not output_root.exists()
