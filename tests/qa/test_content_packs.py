"""Contracts for strict core and optional content-pack manifests."""

from copy import deepcopy
import json
from pathlib import Path

import pytest

from vibesnake.content.packs import (
    CONTENT_PACK_SCHEMA_VERSION,
    CORE_PACK_ID,
    CURRENT_RULESET_ID,
    CURRENT_RULESET_VERSION,
    ContentPackError,
    check_pack_manifest,
    evaluate_pack_compatibility,
    load_pack_manifest,
    render_pack_manifest,
    resolve_pack_set,
    validate_pack_manifest,
)


POLICY_HASH = "a" * 64
CORE_CREDIT = {
    "id": "core-rights",
    "source": "project-owned fixture",
    "license": "MIT",
    "attribution": "none",
    "reviewEvidence": "fixture approval record",
}
RADIO_CREDIT = {
    "id": "radio-rights",
    "source": "licensed fixture",
    "license": "CC-BY-4.0",
    "attribution": "Fixture Artist",
    "reviewEvidence": "fixture license review",
}


def _asset(
    path: str,
    *,
    pack_id: str,
    role: str,
    runtime_use: str,
    sha256: str,
    rights: dict[str, str],
    media_type: str = "application/json",
    size: int = 10,
) -> dict[str, object]:
    return {
        "id": f"asset:{path}",
        "path": path,
        "mediaType": media_type,
        "bytes": size,
        "sha256": sha256,
        "integrityStatus": "valid",
        "integrityDetail": "fixture is valid",
        "role": role,
        "packId": pack_id,
        "runtimeUse": runtime_use,
        "shipStatus": "approved",
        "exportEligible": True,
        "rights": {
            "status": "cleared",
            "source": rights["source"],
            "license": rights["license"],
            "attribution": rights["attribution"],
            "reviewNote": rights["reviewEvidence"],
        },
        "policyRule": "fixture-rule",
        "duplicateOf": None,
    }


def _inventory() -> dict[str, object]:
    assets = [
        _asset(
            "config/core.json",
            pack_id=CORE_PACK_ID,
            role="core-config",
            runtime_use="required",
            sha256="1" * 64,
            rights=CORE_CREDIT,
        ),
        _asset(
            "images/logo.png",
            pack_id=CORE_PACK_ID,
            role="core-image",
            runtime_use="optional",
            sha256="2" * 64,
            rights=CORE_CREDIT,
            media_type="image/png",
            size=20,
        ),
        _asset(
            "audio/radio/flow/track-01.mp3",
            pack_id="vibesnake.radio.flow-signal",
            role="radio-track",
            runtime_use="optional",
            sha256="3" * 64,
            rights=RADIO_CREDIT,
            media_type="audio/mpeg",
            size=30,
        ),
    ]
    return {
        "schemaVersion": 1,
        "assetRoot": "assets",
        "policyPath": "config/content_policy.json",
        "policySha256": POLICY_HASH,
        "fileCount": len(assets),
        "totalBytes": sum(int(asset["bytes"]) for asset in assets),
        "summary": {},
        "assets": assets,
    }


def _file(asset: dict[str, object], credit_id: str) -> dict[str, object]:
    return {
        "id": asset["id"],
        "path": asset["path"],
        "mediaType": asset["mediaType"],
        "bytes": asset["bytes"],
        "sha256": asset["sha256"],
        "role": asset["role"],
        "runtimeUse": asset["runtimeUse"],
        "creditId": credit_id,
    }


def _compatibility() -> dict[str, object]:
    return {
        "gameVersion": {
            "minInclusive": "0.3.0",
            "maxExclusive": "1.1.0",
        },
        "ruleset": {
            "id": CURRENT_RULESET_ID,
            "minInclusive": CURRENT_RULESET_VERSION,
            "maxExclusive": CURRENT_RULESET_VERSION + 1,
        },
    }


def _binding() -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "assetRoot": "assets",
        "policySha256": POLICY_HASH,
    }


def _core_manifest(inventory: dict[str, object]) -> dict[str, object]:
    assets = inventory["assets"]
    return {
        "schemaVersion": CONTENT_PACK_SCHEMA_VERSION,
        "id": CORE_PACK_ID,
        "version": "1.0.0",
        "kind": "core",
        "displayName": "Vibe Snake Core",
        "description": "Required offline content fixture.",
        "compatibility": _compatibility(),
        "inventory": _binding(),
        "dependencies": [],
        "files": [
            _file(assets[0], CORE_CREDIT["id"]),
            _file(assets[1], CORE_CREDIT["id"]),
        ],
        "credits": [deepcopy(CORE_CREDIT)],
        "radio": None,
    }


def _radio_manifest(inventory: dict[str, object]) -> dict[str, object]:
    asset = inventory["assets"][2]
    return {
        "schemaVersion": CONTENT_PACK_SCHEMA_VERSION,
        "id": "vibesnake.radio.flow-signal",
        "version": "1.0.0",
        "kind": "radio",
        "displayName": "The Flow Signal",
        "description": "Optional radio fixture.",
        "compatibility": _compatibility(),
        "inventory": _binding(),
        "dependencies": [
            {
                "id": CORE_PACK_ID,
                "minInclusive": "1.0.0",
                "maxExclusive": "2.0.0",
            }
        ],
        "files": [_file(asset, RADIO_CREDIT["id"])],
        "credits": [deepcopy(RADIO_CREDIT)],
        "radio": {
            "stationId": "flow-signal",
            "stationName": "The Flow Signal",
            "trackIds": [asset["id"]],
        },
    }


def test_core_and_radio_manifests_validate_against_exact_inventory_allowlists():
    inventory = _inventory()
    core = validate_pack_manifest(_core_manifest(inventory), inventory)
    radio = validate_pack_manifest(_radio_manifest(inventory), inventory)

    assert core["id"] == CORE_PACK_ID
    assert core["kind"] == "core"
    assert radio["kind"] == "radio"
    assert radio["radio"]["trackIds"] == ["asset:audio/radio/flow/track-01.mp3"]


def test_validation_returns_a_detached_manifest():
    inventory = _inventory()
    source = _core_manifest(inventory)
    validated = validate_pack_manifest(source, inventory)

    source["files"][0]["path"] = "changed.json"

    assert validated["files"][0]["path"] == "config/core.json"


def test_load_check_and_render_enforce_canonical_manifest_encoding(tmp_path: Path):
    inventory = _inventory()
    manifest = _core_manifest(inventory)
    path = tmp_path / "core.json"
    path.write_text(render_pack_manifest(manifest), encoding="utf-8")

    assert load_pack_manifest(path, inventory) == manifest
    assert check_pack_manifest(path, inventory) == manifest

    path.write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(ContentPackError, match="canonically encoded"):
        check_pack_manifest(path, inventory)


def test_load_reports_missing_and_malformed_manifests(tmp_path: Path):
    inventory = _inventory()
    with pytest.raises(ContentPackError, match="does not exist"):
        load_pack_manifest(tmp_path / "missing.json", inventory)

    malformed = tmp_path / "malformed.json"
    malformed.write_text("{", encoding="utf-8")
    with pytest.raises(ContentPackError, match="unreadable"):
        load_pack_manifest(malformed, inventory)

    duplicate = tmp_path / "duplicate.json"
    duplicate.write_text(
        '{"schemaVersion":1,"schemaVersion":1}',
        encoding="utf-8",
    )
    with pytest.raises(ContentPackError, match="duplicate JSON field"):
        load_pack_manifest(duplicate, inventory)


@pytest.mark.parametrize(
    ("change", "message"),
    [
        (lambda manifest: manifest.update(schemaVersion=2), "unsupported content pack"),
        (lambda manifest: manifest.update(schemaVersion=True), "unsupported content pack"),
        (lambda manifest: manifest.update(unknown=True), "invalid fields"),
        (lambda manifest: manifest.update(id="VibeSnake.Core"), "lowercase"),
        (lambda manifest: manifest.update(version="1.0"), "MAJOR.MINOR.PATCH"),
        (
            lambda manifest: manifest["compatibility"]["gameVersion"].update(
                minInclusive="1.0.0", maxExclusive="1.0.0"
            ),
            "non-empty version range",
        ),
        (
            lambda manifest: manifest["compatibility"]["ruleset"].update(
                minInclusive=CURRENT_RULESET_VERSION,
                maxExclusive=CURRENT_RULESET_VERSION,
            ),
            "ruleset range",
        ),
        (
            lambda manifest: manifest["files"][0].update(path="../core.json"),
            "unsafe path segment",
        ),
        (
            lambda manifest: manifest["files"][0].update(creditId="missing"),
            "unknown credit",
        ),
        (
            lambda manifest: manifest["dependencies"].append(
                {
                    "id": CORE_PACK_ID,
                    "minInclusive": "1.0.0",
                    "maxExclusive": "2.0.0",
                }
            ),
            "depend on itself",
        ),
    ],
)
def test_manifest_rejects_unsafe_or_ambiguous_structure(change, message):
    inventory = _inventory()
    manifest = _core_manifest(inventory)
    change(manifest)

    with pytest.raises(ContentPackError, match=message):
        validate_pack_manifest(manifest, inventory)


def test_manifest_requires_the_complete_approved_pack_allowlist():
    inventory = _inventory()
    manifest = _core_manifest(inventory)
    manifest["files"].pop()

    with pytest.raises(ContentPackError, match="missing asset:images/logo.png"):
        validate_pack_manifest(manifest, inventory)

    manifest = _core_manifest(inventory)
    inventory["assets"][0]["exportEligible"] = False
    with pytest.raises(ContentPackError, match="unexpected asset:config/core.json"):
        validate_pack_manifest(manifest, inventory)


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("sha256", "f" * 64),
        ("bytes", 99),
        ("mediaType", "text/plain"),
        ("role", "wrong-role"),
    ],
)
def test_manifest_file_metadata_must_match_inventory(field, value):
    inventory = _inventory()
    manifest = _core_manifest(inventory)
    manifest["files"][0][field] = value

    with pytest.raises(ContentPackError, match=f"{field} does not match"):
        validate_pack_manifest(manifest, inventory)


def test_manifest_runtime_use_must_match_inventory():
    inventory = _inventory()
    manifest = _core_manifest(inventory)
    manifest["files"][1]["runtimeUse"] = "required"

    with pytest.raises(ContentPackError, match="runtimeUse does not match"):
        validate_pack_manifest(manifest, inventory)


@pytest.mark.parametrize(
    ("inventory_field", "value", "message"),
    [
        ("duplicateOf", "asset:other", "approved unique valid export"),
        ("integrityStatus", "invalid", "approved unique valid export"),
        ("shipStatus", "blocked", "approved unique valid export"),
    ],
)
def test_manifest_rejects_ineligible_inventory_evidence(
    inventory_field,
    value,
    message,
):
    inventory = _inventory()
    inventory["assets"][0][inventory_field] = value

    with pytest.raises(ContentPackError, match=message):
        validate_pack_manifest(_core_manifest(inventory), inventory)


def test_manifest_credit_must_reproduce_cleared_inventory_rights():
    inventory = _inventory()
    manifest = _core_manifest(inventory)
    manifest["credits"][0]["license"] = "UNKNOWN"
    with pytest.raises(ContentPackError, match="license does not match"):
        validate_pack_manifest(manifest, inventory)

    inventory = _inventory()
    inventory["assets"][0]["rights"]["status"] = "unverified"
    with pytest.raises(ContentPackError, match="cleared rights"):
        validate_pack_manifest(_core_manifest(inventory), inventory)


@pytest.mark.parametrize(
    ("change", "message"),
    [
        (lambda manifest: manifest.update(id="vibesnake.radio.other"), "stationId"),
        (lambda manifest: manifest.update(dependencies=[]), "depend only"),
        (
            lambda manifest: manifest["files"][0].update(runtimeUse="required"),
            "optional runtime use",
        ),
        (
            lambda manifest: manifest["radio"].update(trackIds=[]),
            "at least one track",
        ),
        (
            lambda manifest: manifest["radio"].update(
                trackIds=[
                    "asset:audio/radio/flow/track-01.mp3",
                    "asset:audio/radio/flow/track-01.mp3",
                ]
            ),
            "must be unique",
        ),
        (
            lambda manifest: manifest["radio"].update(trackIds=[1]),
            "must be a non-empty string",
        ),
        (
            lambda manifest: manifest["files"][0].update(role="station-badge"),
            "role radio-track",
        ),
    ],
)
def test_radio_contract_rejects_incomplete_station_manifests(change, message):
    inventory = _inventory()
    manifest = _radio_manifest(inventory)
    change(manifest)

    with pytest.raises(ContentPackError, match=message):
        validate_pack_manifest(manifest, inventory)


@pytest.mark.parametrize(
    ("game_version", "ruleset_id", "ruleset_version", "installed", "code"),
    [
        ("0.2.9", CURRENT_RULESET_ID, CURRENT_RULESET_VERSION, {CORE_PACK_ID: "1.0.0"}, "game-version-too-old"),
        ("1.1.0", CURRENT_RULESET_ID, CURRENT_RULESET_VERSION, {CORE_PACK_ID: "1.0.0"}, "game-version-too-new"),
        ("0.3.0", "other", CURRENT_RULESET_VERSION, {CORE_PACK_ID: "1.0.0"}, "ruleset-mismatch"),
        ("0.3.0", CURRENT_RULESET_ID, CURRENT_RULESET_VERSION - 1, {CORE_PACK_ID: "1.0.0"}, "rules-version-too-old"),
        ("0.3.0", CURRENT_RULESET_ID, CURRENT_RULESET_VERSION + 1, {CORE_PACK_ID: "1.0.0"}, "rules-version-too-new"),
        ("0.3.0", CURRENT_RULESET_ID, CURRENT_RULESET_VERSION, {}, "missing-dependency"),
        ("0.3.0", CURRENT_RULESET_ID, CURRENT_RULESET_VERSION, {CORE_PACK_ID: "0.9.0"}, "dependency-version-too-old"),
        ("0.3.0", CURRENT_RULESET_ID, CURRENT_RULESET_VERSION, {CORE_PACK_ID: "2.0.0"}, "dependency-version-too-new"),
        ("0.3.0", CURRENT_RULESET_ID, CURRENT_RULESET_VERSION, {CORE_PACK_ID: "1.0.0"}, "compatible"),
    ],
)
def test_compatibility_reports_actionable_range_failures(
    game_version,
    ruleset_id,
    ruleset_version,
    installed,
    code,
):
    inventory = _inventory()
    radio = validate_pack_manifest(_radio_manifest(inventory), inventory)

    result = evaluate_pack_compatibility(
        radio,
        game_version=game_version,
        ruleset_id=ruleset_id,
        ruleset_version=ruleset_version,
        installed_packs=installed,
    )

    assert result.code == code
    assert result.compatible is (code == "compatible")


def test_pack_set_keeps_core_ready_when_optional_content_is_missing_or_invalid():
    inventory = _inventory()
    core = _core_manifest(inventory)

    no_optional = resolve_pack_set(core, [], inventory, game_version="0.3.0")
    assert no_optional.core_ready
    assert no_optional.accepted_optional == ()
    assert no_optional.rejected_optional == {}

    radio = _radio_manifest(inventory)
    accepted = resolve_pack_set(core, [radio], inventory, game_version="0.3.0")
    assert accepted.core_ready
    assert accepted.accepted_optional == ("vibesnake.radio.flow-signal",)

    radio["files"][0]["sha256"] = "f" * 64
    rejected = resolve_pack_set(core, [radio], inventory, game_version="0.3.0")
    assert rejected.core_ready
    assert rejected.accepted_optional == ()
    assert rejected.rejected_optional["vibesnake.radio.flow-signal"].code == ("invalid-pack")


def test_pack_set_rejects_incompatible_optional_without_blocking_core():
    inventory = _inventory()
    radio = _radio_manifest(inventory)
    radio["compatibility"]["gameVersion"]["minInclusive"] = "0.4.0"

    resolution = resolve_pack_set(
        _core_manifest(inventory),
        [radio],
        inventory,
        game_version="0.3.0",
    )

    assert resolution.core_ready
    assert resolution.accepted_optional == ()
    assert resolution.rejected_optional["vibesnake.radio.flow-signal"].code == ("game-version-too-old")


def test_pack_set_rejects_duplicate_optional_ids_without_accepting_either_copy():
    inventory = _inventory()
    radio = _radio_manifest(inventory)

    resolution = resolve_pack_set(
        _core_manifest(inventory),
        [radio, deepcopy(radio), deepcopy(radio)],
        inventory,
        game_version="0.3.0",
    )

    assert resolution.core_ready
    assert resolution.accepted_optional == ()
    assert resolution.rejected_optional["vibesnake.radio.flow-signal"].code == ("invalid-pack")


def test_pack_set_treats_an_invalid_or_incompatible_core_as_fatal():
    inventory = _inventory()
    invalid_core = _core_manifest(inventory)
    invalid_core["files"][0]["sha256"] = "f" * 64
    with pytest.raises(ContentPackError):
        resolve_pack_set(invalid_core, [], inventory, game_version="0.3.0")

    incompatible_core = _core_manifest(inventory)
    incompatible_core["compatibility"]["gameVersion"]["minInclusive"] = "0.4.0"
    result = resolve_pack_set(
        incompatible_core,
        [_radio_manifest(inventory)],
        inventory,
        game_version="0.3.0",
    )
    assert not result.core_ready
    assert result.rejected_optional["vibesnake.radio.flow-signal"].code == ("core-unavailable")
