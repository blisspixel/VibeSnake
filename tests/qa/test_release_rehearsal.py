"""Contracts for the V090-10 release and rollback rehearsal handoff."""

from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path

from scripts.check_release_rehearsal import (
    ARTIFACT_PLATFORMS,
    AUTHORITY_OPERATION_IDS,
    CONTRACT_PATH,
    PLATFORM_OPERATION_IDS,
    _fixture_set_sha,
    validate_contract,
    validate_release_rehearsal,
)


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def _write_evidence(path: Path, value: bytes = b"retained rehearsal evidence") -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(value)
    return path.as_posix()


def _record(path: Path) -> dict[str, object]:
    candidate_paths: dict[str, str] = {}
    previous_paths: dict[str, str] = {}
    manifest_paths: dict[str, str] = {}
    candidate_hashes: dict[str, str] = {}
    previous_hashes: dict[str, str] = {}
    manifest_hashes: dict[str, str] = {}
    retained_paths: set[str] = set()
    for index, platform in enumerate(ARTIFACT_PLATFORMS):
        candidate_relative = f"staged/{platform}/candidate.bin"
        previous_relative = f"previous/{platform}/previous.bin"
        manifest_relative = f"staged/{platform}/artifact-manifest.json"
        _write_evidence(path.parent / candidate_relative, f"candidate-{platform}".encode())
        _write_evidence(path.parent / previous_relative, f"previous-{platform}".encode())
        _write_evidence(path.parent / manifest_relative, f"manifest-{platform}".encode())
        candidate_paths[platform] = candidate_relative
        previous_paths[platform] = previous_relative
        manifest_paths[platform] = manifest_relative
        candidate_hashes[platform] = hashlib.sha256((path.parent / candidate_relative).read_bytes()).hexdigest()
        previous_hashes[platform] = hashlib.sha256((path.parent / previous_relative).read_bytes()).hexdigest()
        manifest_hashes[platform] = hashlib.sha256((path.parent / manifest_relative).read_bytes()).hexdigest()
        retained_paths.update((candidate_relative, previous_relative, manifest_relative))

    decision_path = "evidence/release-materials-decision.json"
    _write_evidence(path.parent / decision_path)
    retained_paths.add(decision_path)
    fixture_paths = ["fixtures/preferences-v5.json", "fixtures/personal-best-v1.json"]
    for relative_path in fixture_paths:
        _write_evidence(path.parent / relative_path, relative_path.encode())
    retained_paths.update(fixture_paths)

    platform_results: list[dict[str, object]] = []
    for platform in ARTIFACT_PLATFORMS:
        operation_paths: dict[str, list[str]] = {}
        for operation_id in PLATFORM_OPERATION_IDS:
            relative_path = f"evidence/{platform}/{operation_id}.json"
            _write_evidence(path.parent / relative_path)
            retained_paths.add(relative_path)
            operation_paths[operation_id] = [relative_path]
        protected_hash = hashlib.sha256(f"protected-{platform}".encode()).hexdigest()
        platform_results.append(
            {
                "platformId": platform,
                "operationResults": {operation_id: "pass" for operation_id in PLATFORM_OPERATION_IDS},
                "evidencePathsByOperation": operation_paths,
                "protectedUserDataSha256Before": protected_hash,
                "protectedUserDataSha256After": protected_hash,
            }
        )

    withdrawal_path = "evidence/withdrawal.json"
    _write_evidence(path.parent / withdrawal_path)
    retained_paths.add(withdrawal_path)
    authority_records: list[dict[str, object]] = []
    for operation_id in AUTHORITY_OPERATION_IDS:
        relative_path = f"evidence/authority/{operation_id}.json"
        _write_evidence(path.parent / relative_path)
        retained_paths.add(relative_path)
        authority_records.append(
            {
                "operationId": operation_id,
                "roleId": f"release-{operation_id}-role",
                "authorizationVerified": True,
                "evidencePaths": [relative_path],
            }
        )

    return {
        "schemaVersion": 1,
        "kind": "vibesnake-release-rehearsal-record-v1",
        "rehearsalId": "candidate-rehearsal-001",
        "sourceRevision": "a" * 40,
        "appVersion": "0.3.0-alpha.1",
        "previousVersion": "0.2.1",
        "stagedLocationId": "controlled-stage-001",
        "executedUtc": "2026-08-09T18:00:00Z",
        "candidateArtifactSha256ByPlatform": candidate_hashes,
        "candidateArtifactPathsByPlatform": candidate_paths,
        "previousArtifactSha256ByPlatform": previous_hashes,
        "previousArtifactPathsByPlatform": previous_paths,
        "candidateManifestSha256ByPlatform": manifest_hashes,
        "candidateManifestPathsByPlatform": manifest_paths,
        "releaseMaterialsDecisionSha256": hashlib.sha256((path.parent / decision_path).read_bytes()).hexdigest(),
        "releaseMaterialsDecisionPath": decision_path,
        "migrationFixtureSetSha256": _fixture_set_sha(fixture_paths, path.parent),
        "migrationFixturePaths": fixture_paths,
        "platformResults": platform_results,
        "withdrawalResult": {
            "candidateUnavailable": True,
            "previousArtifactRestored": True,
            "userDataPreserved": True,
            "communicationPrepared": True,
            "evidencePaths": [withdrawal_path],
        },
        "authorityRecords": authority_records,
        "retainedFileSha256": {
            relative_path: hashlib.sha256((path.parent / relative_path).read_bytes()).hexdigest()
            for relative_path in sorted(retained_paths)
        },
    }


def test_repository_release_rehearsal_handoff_is_exact_and_pending() -> None:
    errors, evidence = validate_release_rehearsal()

    assert errors == []
    assert evidence["passed"] is True
    assert evidence["protocolQualified"] is True
    assert evidence["artifactPlatformCount"] == 3
    assert evidence["platformOperationCount"] == 11
    assert evidence["requiredPlatformOperationCellCount"] == 33
    assert evidence["authorityOperationCount"] == 4
    assert evidence["recordSupplied"] is False
    assert evidence["rehearsalComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert len(evidence["pendingGates"]) == 6


def test_contract_rejects_a_missing_operation(tmp_path: Path) -> None:
    contract = copy.deepcopy(json.loads(CONTRACT_PATH.read_text(encoding="utf-8")))
    contract["platformOperationIds"].remove("rollback")
    path = tmp_path / "contract.json"
    _write_json(path, contract)

    errors, _ = validate_contract(path)

    assert any("contract.platformOperationIds must be" in error for error in errors)


def test_complete_staged_release_and_rollback_rehearsal_closes_the_gate(tmp_path: Path) -> None:
    record_path = tmp_path / "rehearsal" / "record.json"
    _write_json(record_path, _record(record_path))

    errors, evidence = validate_release_rehearsal(
        record_path=record_path,
        expected_revision="a" * 40,
    )

    assert errors == []
    assert evidence["recordSupplied"] is True
    assert evidence["rehearsalComplete"] is True
    assert evidence["releaseAcceptance"] is True
    assert evidence["pendingGates"] == []


def test_failed_rollback_changed_data_and_tampered_evidence_block_acceptance(tmp_path: Path) -> None:
    record_path = tmp_path / "rehearsal" / "record.json"
    record = _record(record_path)
    record["platformResults"][0]["operationResults"]["rollback"] = "fail"
    record["platformResults"][1]["protectedUserDataSha256After"] = "f" * 64
    _write_json(record_path, record)
    tampered_path = record_path.parent / record["withdrawalResult"]["evidencePaths"][0]
    tampered_path.write_bytes(b"tampered")

    errors, evidence = validate_release_rehearsal(
        record_path=record_path,
        expected_revision="a" * 40,
    )

    assert evidence["rehearsalComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert any("rollback blocks rehearsal" in error for error in errors)
    assert any("changed protected user data" in error for error in errors)
    assert any("retained rehearsal file hash mismatch" in error for error in errors)
