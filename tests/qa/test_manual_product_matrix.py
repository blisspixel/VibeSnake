"""Contracts for the V090-07 manual product matrix handoff."""

from __future__ import annotations

import copy
import json
from pathlib import Path

from scripts.check_manual_product_matrix import (
    CONTRACT_PATH,
    INPUT_DEVICES,
    PLATFORM_ROWS,
    REQUIRED_FLOWS,
    SETTINGS_PROFILES,
    validate_contract,
    validate_manual_product_matrix,
)


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def _write_session(path: Path, session: dict[str, object]) -> None:
    _write_json(path, session)
    for result in session["results"]:
        for relative_path in result["evidencePaths"]:
            evidence_path = path.parent / relative_path
            evidence_path.parent.mkdir(parents=True, exist_ok=True)
            evidence_path.write_bytes(b"retained manual evidence")


def _session(platform: str, index: int) -> dict[str, object]:
    artifact_digit = 2 if platform.startswith("macos-universal") else index + 1
    return {
        "schemaVersion": 1,
        "kind": "vibesnake-manual-product-matrix-session-v1",
        "sessionId": f"product-matrix-{index:03d}",
        "candidateRevision": "a" * 40,
        "artifactSha256": str(artifact_digit) * 64,
        "appVersion": "0.9.0",
        "platformRowId": platform,
        "operatingSystemVersion": "qualified-os-version",
        "hardwareClass": "declared-hardware-class",
        "renderer": "gl-compatibility",
        "inputDeviceIds": [INPUT_DEVICES[index][0]],
        "settingsProfileIds": list(SETTINGS_PROFILES[index * 2 : (index + 1) * 2]),
        "executedUtc": f"2026-08-0{index + 1}T12:00:00Z",
        "results": [
            {
                "flowId": flow_id,
                "result": "pass",
                "evidencePaths": [f"evidence/{platform}/{flow_id}.png"],
            }
            for flow_id in REQUIRED_FLOWS
        ],
    }


def test_repository_manual_product_matrix_handoff_is_exact_and_pending() -> None:
    errors, evidence = validate_manual_product_matrix()

    assert errors == []
    assert evidence["passed"] is True
    assert evidence["protocolQualified"] is True
    assert evidence["platformRowCount"] == 4
    assert evidence["requiredFlowCount"] == 36
    assert evidence["requiredPlatformFlowCellCount"] == 144
    assert evidence["inputDeviceCount"] == 4
    assert evidence["settingsProfileCount"] == 8
    assert evidence["manualSessionCount"] == 0
    assert evidence["manualExecutionComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert len(evidence["pendingGates"]) == 5


def test_contract_rejects_a_missing_required_flow(tmp_path: Path) -> None:
    contract = copy.deepcopy(json.loads(CONTRACT_PATH.read_text(encoding="utf-8")))
    contract["requiredFlows"].remove("quit")
    path = tmp_path / "contract.json"
    _write_json(path, contract)

    errors, _ = validate_contract(path)

    assert any("contract.requiredFlows must be" in error for error in errors)


def test_complete_retained_sessions_close_every_matrix_dimension(tmp_path: Path) -> None:
    sessions = tmp_path / "sessions"
    for index, (platform, _, _) in enumerate(PLATFORM_ROWS):
        _write_session(sessions / f"session-{index}.json", _session(platform, index))

    errors, evidence = validate_manual_product_matrix(sessions_directory=sessions)

    assert errors == []
    assert evidence["manualSessionCount"] == 4
    assert evidence["completedPlatformFlowCellCount"] == 144
    assert evidence["observedInputDevices"] == sorted(item[0] for item in INPUT_DEVICES)
    assert evidence["observedSettingsProfiles"] == sorted(SETTINGS_PROFILES)
    assert evidence["failedOrBlockedResultCount"] == 0
    assert evidence["manualExecutionComplete"] is True
    assert evidence["releaseAcceptance"] is True
    assert evidence["pendingGates"] == []


def test_incomplete_or_failed_session_cannot_claim_acceptance(tmp_path: Path) -> None:
    sessions = tmp_path / "sessions"
    for index, (platform, _, _) in enumerate(PLATFORM_ROWS):
        document = _session(platform, index)
        if index == 0:
            document["results"][0]["result"] = "fail"
        if index == 1:
            document["results"] = document["results"][:-1]
        _write_session(sessions / f"session-{index}.json", document)
    missing_evidence = sessions / "evidence" / PLATFORM_ROWS[2][0] / f"{REQUIRED_FLOWS[0]}.png"
    missing_evidence.unlink()

    errors, evidence = validate_manual_product_matrix(sessions_directory=sessions)

    assert evidence["manualExecutionComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert any("missing passing flows" in error for error in errors)
    assert any("missing retained files" in error for error in errors)
    assert "manual matrix contains failed or blocked required flows" in errors
