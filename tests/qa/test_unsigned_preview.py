"""Contracts for fail-closed unsigned native alpha assembly."""

from __future__ import annotations

import hashlib
import json
import stat
from pathlib import Path
from zipfile import ZIP_STORED, ZipFile, ZipInfo

import pytest

import scripts.assemble_unsigned_preview as unsigned_preview_module
from scripts.assemble_unsigned_preview import EXTENSIONS, PLATFORMS, assemble_unsigned_preview


VERSION = "0.3.0-alpha.1"
REVISION = "a" * 40


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def _radio_fixture(root: Path) -> Path:
    radio_root = root / "radio"
    radio_root.mkdir()
    track_id = "asset:audio/radio/flow_signal_track.mp3"
    track_path = "audio/radio/flow_signal_track.mp3"
    track = b"approved radio track"
    manifest = {
        "schemaVersion": 1,
        "id": "vibesnake.radio.flow-signal",
        "version": "1.0.0",
        "kind": "radio",
        "files": [
            {
                "id": track_id,
                "path": track_path,
                "bytes": len(track),
                "sha256": hashlib.sha256(track).hexdigest(),
            }
        ],
        "radio": {
            "stationId": "flow_signal",
            "stationName": "The Flow Signal",
            "trackIds": [track_id],
        },
    }
    manifest_path = radio_root / "pack.json"
    _write_json(manifest_path, manifest)
    pack_name = "vibesnake.radio.flow-signal-1.0.0.vibesnake-pack.zip"
    pack_path = radio_root / pack_name
    with ZipFile(pack_path, "w", compression=ZIP_STORED) as archive:
        for name, value in (("pack.json", manifest_path.read_bytes()), (track_path, track)):
            entry = ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            entry.compress_type = ZIP_STORED
            entry.create_system = 3
            entry.external_attr = (stat.S_IFREG | 0o644) << 16
            archive.writestr(entry, value)
    assembly_path = radio_root / "radio_pack_assembly.json"
    _write_json(
        assembly_path,
        {
            "schemaVersion": 1,
            "kind": "approved-radio-pack-assembly-v1",
            "passed": True,
            "releaseApproved": True,
            "packId": "vibesnake.radio.flow-signal",
            "packVersion": "1.0.0",
            "stationId": "flow_signal",
            "stationName": "The Flow Signal",
            "curationDecisionStatus": "approved-for-alpha-release",
            "inventorySha256": "b" * 64,
            "curationSha256": "c" * 64,
            "manifestSha256": _sha256(manifest_path),
            "packFileName": pack_name,
            "packBytes": pack_path.stat().st_size,
            "packSha256": _sha256(pack_path),
            "trackCount": 1,
            "trackIds": [track_id],
        },
    )
    (radio_root / "SHA256SUMS.txt").write_text(
        "".join(f"{_sha256(path)}  {path.name}\n" for path in sorted((pack_path, manifest_path, assembly_path))),
        encoding="utf-8",
    )
    return radio_root


def _fixture(root: Path) -> tuple[Path, Path, Path, Path, Path, Path]:
    version_root = root / "source"
    version_root.mkdir()
    (version_root / "VERSION").write_bytes((VERSION + "\n").encode())
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
    radio_root = _radio_fixture(root)
    return channel_root, provenance_root, radio_root, matrix_path, version_root, root / "preview"


def _refresh_radio_checksums(radio_root: Path) -> None:
    paths = (
        next(radio_root.glob("*.vibesnake-pack.zip")),
        radio_root / "pack.json",
        radio_root / "radio_pack_assembly.json",
    )
    (radio_root / "SHA256SUMS.txt").write_text(
        "".join(f"{_sha256(path)}  {path.name}\n" for path in sorted(paths)),
        encoding="utf-8",
    )


def _assemble(root: Path, tag: str = f"v{VERSION}") -> tuple[list[str], dict[str, object]]:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(root)
    return assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
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
    assert evidence["radioPack"]["stationId"] == "flow_signal"
    output_root = tmp_path / "preview"
    assert (output_root / "unsigned_preview_manifest.json").is_file()
    checksums = (output_root / "SHA256SUMS.txt").read_text(encoding="utf-8")
    assert "-windows-x64-unsigned-preview.zip" in checksums
    assert "-macos-universal-unsigned-preview.zip" in checksums
    assert "-linux-x64-unsigned-preview.tar.gz" in checksums
    assert ".vibesnake-pack.zip" in checksums
    assert "qualification" not in checksums


def test_nonmatching_or_nonalpha_tag_is_rejected_without_output(tmp_path: Path) -> None:
    errors, evidence = _assemble(tmp_path, tag="v0.3.0")

    assert evidence["passed"] is False
    assert any("tag must exactly match" in error for error in errors)
    assert not (tmp_path / "preview").exists()


def test_stable_version_cannot_use_unsigned_preview_path(tmp_path: Path) -> None:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    (version_root / "VERSION").write_bytes(b"0.3.0\n")

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
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
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    windows_root = channel_root / "vibesnake-windows-x64-unsigned-channel-shape"
    package_path = next(windows_root.glob("*qualification.zip"))
    package_path.write_bytes(b"tampered")
    (windows_root / "unexpected.txt").write_text("unexpected\n", encoding="utf-8")

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
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
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    missing = provenance_root / "vibesnake-linux-x64-provenance" / "attestation.jsonl"
    missing.unlink()

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
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
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    manifest_path = channel_root / "vibesnake-macos-universal-unsigned-channel-shape" / "artifact-manifest.json"
    manifest_path.unlink()

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("missing macos-universal artifact manifest" in error for error in errors)
    assert not output_root.exists()


def test_missing_approved_radio_pack_blocks_preview_publication(tmp_path: Path) -> None:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    next(radio_root.glob("*.vibesnake-pack.zip")).unlink()

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("missing approved radio pack" in error for error in errors)
    assert not output_root.exists()


def test_tampered_radio_archive_or_extra_content_evidence_is_rejected(tmp_path: Path) -> None:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    next(radio_root.glob("*.vibesnake-pack.zip")).write_bytes(b"tampered radio")
    (radio_root / "unexpected.txt").write_text("unexpected\n", encoding="utf-8")

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("radio-pack byte count changed" in error for error in errors)
    assert any("unexpected file set" in error for error in errors)
    assert not output_root.exists()


def test_duplicate_radio_evidence_fields_fail_closed(tmp_path: Path) -> None:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    assembly_path = radio_root / "radio_pack_assembly.json"
    source = assembly_path.read_text(encoding="utf-8")
    assembly_path.write_text(
        source.replace('"schemaVersion": 1,', '"schemaVersion": 1, "schemaVersion": 1,'),
        encoding="utf-8",
    )

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("duplicate JSON field" in error for error in errors)
    assert not output_root.exists()


def test_preview_rejects_boolean_schema_versions_in_qualified_evidence(tmp_path: Path) -> None:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    plan_path = channel_root / "vibesnake-windows-x64-unsigned-channel-shape" / "release_output_plan.json"
    plan = json.loads(plan_path.read_text(encoding="utf-8"))
    plan["schemaVersion"] = True
    _write_json(plan_path, plan)
    assembly_path = radio_root / "radio_pack_assembly.json"
    assembly = json.loads(assembly_path.read_text(encoding="utf-8"))
    assembly["schemaVersion"] = True
    _write_json(assembly_path, assembly)
    _refresh_radio_checksums(radio_root)

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("radio-pack assembly.schemaVersion must be 1" in error for error in errors)
    assert any("windows-x64 plan.schemaVersion must be 1" in error for error in errors)
    assert not output_root.exists()


@pytest.mark.parametrize(
    ("mutate_manifest", "mutate_assembly"),
    [
        (lambda manifest: manifest["radio"].update(stationName="Conflicting Name"), lambda _assembly: None),
        (lambda _manifest: None, lambda assembly: assembly.update(trackCount=True)),
    ],
)
def test_radio_track_evidence_requires_exact_types_and_station_identity(
    tmp_path: Path,
    mutate_manifest,
    mutate_assembly,
) -> None:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    manifest_path = radio_root / "pack.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    mutate_manifest(manifest)
    _write_json(manifest_path, manifest)
    assembly_path = radio_root / "radio_pack_assembly.json"
    assembly = json.loads(assembly_path.read_text(encoding="utf-8"))
    mutate_assembly(assembly)
    assembly["manifestSha256"] = _sha256(manifest_path)
    _write_json(assembly_path, assembly)
    _refresh_radio_checksums(radio_root)

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("track evidence does not match" in error for error in errors)
    assert not output_root.exists()


def test_invalid_radio_evidence_filename_never_reads_outside_artifact_root(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    outside = tmp_path / "outside.vibesnake-pack.zip"
    outside.write_bytes(b"outside artifact")
    assembly_path = radio_root / "radio_pack_assembly.json"
    assembly = json.loads(assembly_path.read_text(encoding="utf-8"))
    assembly["packFileName"] = "../outside.vibesnake-pack.zip"
    assembly["packBytes"] = outside.stat().st_size
    assembly["packSha256"] = _sha256(outside)
    _write_json(assembly_path, assembly)
    original_sha256 = unsigned_preview_module._sha256

    def guarded_sha256(path: Path) -> str:
        assert path.resolve().is_relative_to(radio_root.resolve()) or not path.name.endswith(".vibesnake-pack.zip")
        return original_sha256(path)

    monkeypatch.setattr(unsigned_preview_module, "_sha256", guarded_sha256)
    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("packFileName" in error for error in errors)
    assert not output_root.exists()


def test_radio_pack_compressed_budget_is_rechecked_before_publication(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    channel_root, provenance_root, radio_root, matrix_path, version_root, output_root = _fixture(tmp_path)
    monkeypatch.setattr(unsigned_preview_module, "MAXIMUM_RADIO_PACK_COMPRESSED_BYTES", 1)

    errors, evidence = assemble_unsigned_preview(
        channel_root,
        provenance_root,
        radio_root,
        matrix_path,
        version_root,
        f"v{VERSION}",
        REVISION,
        output_root,
    )

    assert evidence["passed"] is False
    assert any("compressed-size budget" in error for error in errors)
    assert not output_root.exists()
