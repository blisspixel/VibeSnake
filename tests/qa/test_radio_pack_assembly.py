"""Contracts for deterministic, reviewed optional radio-pack assembly."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

import pytest

import scripts.assemble_radio_pack as radio_pack_module
from scripts.assemble_radio_pack import RadioPackAssemblyError, assemble_radio_pack
from vibesnake.content.inventory import write_inventory
from vibesnake.content.packs import CURRENT_RULESET_ID, CURRENT_RULESET_VERSION, render_pack_manifest


PACK_ID = "vibesnake.radio.flow-signal"
ASSET_ID = "asset:audio/radio/flow_signal_track.mp3"


def _mp3_frame() -> bytes:
    header = b"\xff\xfb\x90\x64"
    frame_length = (144 * 128_000) // 44_100
    return header + (b"\x00" * (frame_length - len(header)))


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _fixture(root: Path) -> tuple[Path, Path, Path]:
    track_path = root / "assets" / "audio" / "radio" / "flow_signal_track.mp3"
    track_path.parent.mkdir(parents=True)
    track_path.write_bytes(_mp3_frame() * 2)
    policy_path = root / "config" / "content_policy.json"
    policy_path.parent.mkdir(parents=True)
    policy_path.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "assetRoot": "assets",
                "rules": [
                    {
                        "id": "approved-flow-signal-radio",
                        "patterns": ["audio/radio/flow_signal_track.mp3"],
                        "role": "radio-track",
                        "packId": PACK_ID,
                        "runtimeUse": "optional",
                        "shipStatus": "approved",
                        "rights": {
                            "status": "cleared",
                            "source": "test fixture",
                            "license": "Apache-2.0",
                            "attribution": "fixture rights",
                            "reviewNote": "fixture listening review passed",
                        },
                    }
                ],
            }
        )
        + "\n",
        encoding="utf-8",
    )
    inventory_path = root / "config" / "content_inventory.json"
    inventory = write_inventory(root, policy_path=policy_path, inventory_path=inventory_path)
    asset = inventory["assets"][0]
    manifest = {
        "schemaVersion": 1,
        "id": PACK_ID,
        "version": "1.0.0",
        "kind": "radio",
        "displayName": "The Flow Signal",
        "description": "Approved optional radio fixture.",
        "compatibility": {
            "gameVersion": {"minInclusive": "0.3.0", "maxExclusive": "1.0.0"},
            "ruleset": {
                "id": CURRENT_RULESET_ID,
                "minInclusive": CURRENT_RULESET_VERSION,
                "maxExclusive": CURRENT_RULESET_VERSION + 1,
            },
        },
        "inventory": {
            "schemaVersion": 1,
            "assetRoot": "assets",
            "policySha256": inventory["policySha256"],
        },
        "dependencies": [{"id": "vibesnake.core", "minInclusive": "1.0.0", "maxExclusive": "2.0.0"}],
        "files": [
            {
                "id": asset["id"],
                "path": asset["path"],
                "mediaType": asset["mediaType"],
                "bytes": asset["bytes"],
                "sha256": asset["sha256"],
                "role": asset["role"],
                "runtimeUse": asset["runtimeUse"],
                "creditId": "flow-signal-rights",
            }
        ],
        "credits": [
            {
                "id": "flow-signal-rights",
                "source": asset["rights"]["source"],
                "license": asset["rights"]["license"],
                "attribution": asset["rights"]["attribution"],
                "reviewEvidence": asset["rights"]["reviewNote"],
            }
        ],
        "radio": {
            "stationId": "flow_signal",
            "stationName": "The Flow Signal",
            "trackIds": [ASSET_ID],
        },
    }
    manifest_path = root / "config" / "packs" / f"{PACK_ID}.json"
    manifest_path.parent.mkdir()
    manifest_path.write_text(render_pack_manifest(manifest), encoding="utf-8")
    curation_path = root / "config" / "content_curation_v1.json"
    curation_path.write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "planId": "vibesnake-content-curation-v1",
                "inventoryPolicySha256": inventory["policySha256"],
                "decisionStatus": "approved-for-alpha-release",
                "coreMusic": {"pendingAssetIds": [], "approvedAssetIds": [], "rejectedAssetIds": []},
                "stations": [
                    {
                        "id": "flow_signal",
                        "pendingAssetIds": [],
                        "approvedAssetIds": [ASSET_ID],
                        "rejectedAssetIds": [],
                    }
                ],
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    return manifest_path, curation_path, inventory_path


def _assemble(root: Path, output_name: str = "output") -> dict[str, object]:
    manifest, curation, inventory = _fixture(root)
    return assemble_radio_pack(root, manifest, curation, inventory, root / output_name)


def test_approved_radio_pack_is_deterministic_and_install_shaped(tmp_path: Path) -> None:
    first = _assemble(tmp_path / "first")
    second = _assemble(tmp_path / "second")

    assert first["packSha256"] == second["packSha256"]
    assert first["trackIds"] == [ASSET_ID]
    output = tmp_path / "first" / "output"
    archive_path = output / str(first["packFileName"])
    assert archive_path.name.endswith(".vibesnake-pack.zip")
    with ZipFile(archive_path) as archive:
        assert archive.namelist() == ["pack.json", "audio/radio/flow_signal_track.mp3"]
        assert all(entry.date_time == (1980, 1, 1, 0, 0, 0) for entry in archive.infolist())
        assert archive.read("pack.json") == (output / "pack.json").read_bytes()
    checksums = (output / "SHA256SUMS.txt").read_text(encoding="utf-8")
    assert _sha256(archive_path) in checksums
    assert "radio_pack_assembly.json" in checksums


def test_pending_or_incomplete_listening_decisions_block_assembly(tmp_path: Path) -> None:
    manifest, curation, inventory = _fixture(tmp_path)
    value = json.loads(curation.read_text(encoding="utf-8"))
    value["decisionStatus"] = "pending-human-listening-review"
    curation.write_text(json.dumps(value) + "\n", encoding="utf-8")

    with pytest.raises(RadioPackAssemblyError, match="approved-for-alpha-release"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, tmp_path / "output")

    value["decisionStatus"] = "approved-for-alpha-release"
    value["stations"][0]["pendingAssetIds"] = [ASSET_ID]
    value["stations"][0]["approvedAssetIds"] = []
    curation.write_text(json.dumps(value) + "\n", encoding="utf-8")
    with pytest.raises(RadioPackAssemblyError, match="pending listening"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, tmp_path / "output")


def test_station_decisions_cannot_reference_unknown_tracks(tmp_path: Path) -> None:
    manifest, curation, inventory = _fixture(tmp_path)
    value = json.loads(curation.read_text(encoding="utf-8"))
    value["stations"][0]["approvedAssetIds"] = ["asset:audio/radio/other.mp3"]
    curation.write_text(json.dumps(value) + "\n", encoding="utf-8")

    with pytest.raises(RadioPackAssemblyError, match="unknown or non-radio asset IDs"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, tmp_path / "output")


def test_changed_payload_or_existing_output_fails_closed(tmp_path: Path) -> None:
    manifest, curation, inventory = _fixture(tmp_path)
    track = tmp_path / "assets" / "audio" / "radio" / "flow_signal_track.mp3"
    track.write_bytes(track.read_bytes() + b"changed")

    with pytest.raises(RadioPackAssemblyError, match="inventory is stale"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, tmp_path / "output")

    track.write_bytes(_mp3_frame() * 2)
    output = tmp_path / "output"
    output.mkdir()
    with pytest.raises(RadioPackAssemblyError, match="must not already exist"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, output)


def test_duplicate_curation_fields_are_rejected(tmp_path: Path) -> None:
    manifest, curation, inventory = _fixture(tmp_path)
    source = curation.read_text(encoding="utf-8")
    curation.write_text(
        source.replace('"schemaVersion": 1,', '"schemaVersion": 1, "schemaVersion": 1,'), encoding="utf-8"
    )

    with pytest.raises(RadioPackAssemblyError, match="repeats JSON field"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, tmp_path / "output")


def test_oversized_curation_is_rejected_before_parsing(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    manifest, curation, inventory = _fixture(tmp_path)
    monkeypatch.setattr(radio_pack_module, "MAXIMUM_CURATION_BYTES", 1)

    with pytest.raises(RadioPackAssemblyError, match="byte limit"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, tmp_path / "output")


def test_curation_must_account_for_each_radio_asset_once(tmp_path: Path) -> None:
    manifest, curation, inventory = _fixture(tmp_path)
    value = json.loads(curation.read_text(encoding="utf-8"))
    value["stations"][0]["approvedAssetIds"] = []
    curation.write_text(json.dumps(value) + "\n", encoding="utf-8")

    with pytest.raises(RadioPackAssemblyError, match="every inventoried radio asset exactly once"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, tmp_path / "output")

    value["stations"][0]["approvedAssetIds"] = [ASSET_ID]
    value["stations"].append(
        {
            "id": "duplicate_station",
            "pendingAssetIds": [],
            "approvedAssetIds": [ASSET_ID],
            "rejectedAssetIds": [],
        }
    )
    curation.write_text(json.dumps(value) + "\n", encoding="utf-8")
    with pytest.raises(RadioPackAssemblyError, match="multiple stations"):
        assemble_radio_pack(tmp_path, manifest, curation, inventory, tmp_path / "output")


def test_native_radio_pack_size_budgets_are_enforced(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    manifest, curation, inventory = _fixture(tmp_path / "installed")
    monkeypatch.setattr(radio_pack_module, "MAXIMUM_INSTALLED_BYTES", 1)
    with pytest.raises(RadioPackAssemblyError, match="installed-size budget"):
        assemble_radio_pack(
            tmp_path / "installed",
            manifest,
            curation,
            inventory,
            tmp_path / "installed" / "output",
        )

    manifest, curation, inventory = _fixture(tmp_path / "compressed")
    monkeypatch.setattr(radio_pack_module, "MAXIMUM_INSTALLED_BYTES", 120 * 1024 * 1024)
    monkeypatch.setattr(radio_pack_module, "MAXIMUM_COMPRESSED_BYTES", 1)
    with pytest.raises(RadioPackAssemblyError, match="compressed-size budget"):
        assemble_radio_pack(
            tmp_path / "compressed",
            manifest,
            curation,
            inventory,
            tmp_path / "compressed" / "output",
        )
