"""Contracts for the final 1.0 protected-promotion guard."""

from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path

from scripts.check_stable_promotion import (
    ARTIFACT_PLATFORMS,
    CONTRACT_PATH,
    PRESERVED_EVIDENCE_CATEGORIES,
    STABLE_CONTRACT_ACKNOWLEDGEMENTS,
    UPSTREAM_DECISION_IDS,
    validate_contract,
    validate_stable_promotion,
)


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def _write_bytes(path: Path, value: bytes = b"retained stable promotion evidence") -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(value)


def _promotion_record(path: Path) -> dict[str, object]:
    revision = "a" * 40
    artifact_paths: dict[str, str] = {}
    manifest_paths: dict[str, str] = {}
    provenance_paths: dict[str, str] = {}
    checksum_paths: dict[str, str] = {}
    artifact_hashes: dict[str, str] = {}
    manifest_hashes: dict[str, str] = {}
    provenance_hashes: dict[str, str] = {}
    retained_paths: set[str] = set()
    for platform in ARTIFACT_PLATFORMS:
        artifact_path = f"public/{platform}/VibeSnake-1.0.0.bin"
        manifest_path = f"public/{platform}/artifact-manifest.json"
        provenance_path = f"public/{platform}/provenance.json"
        checksum_path = f"public/{platform}/SHA256SUMS"
        _write_bytes(path.parent / artifact_path, f"stable-artifact-{platform}".encode())
        _write_bytes(path.parent / manifest_path, f"stable-manifest-{platform}".encode())
        _write_bytes(path.parent / provenance_path, f"stable-provenance-{platform}".encode())
        artifact_hash = hashlib.sha256((path.parent / artifact_path).read_bytes()).hexdigest()
        _write_bytes(path.parent / checksum_path, f"{artifact_hash}  VibeSnake-1.0.0.bin\n".encode())
        artifact_paths[platform] = artifact_path
        manifest_paths[platform] = manifest_path
        provenance_paths[platform] = provenance_path
        checksum_paths[platform] = checksum_path
        artifact_hashes[platform] = artifact_hash
        manifest_hashes[platform] = hashlib.sha256((path.parent / manifest_path).read_bytes()).hexdigest()
        provenance_hashes[platform] = hashlib.sha256((path.parent / provenance_path).read_bytes()).hexdigest()
        retained_paths.update((artifact_path, manifest_path, provenance_path, checksum_path))

    optional_pack_path = "public/optional/vibesnake-radio-pack.bin"
    optional_manifest_path = "public/optional/pack.json"
    _write_bytes(path.parent / optional_pack_path, b"approved optional content")
    _write_bytes(path.parent / optional_manifest_path, b"approved optional manifest")
    retained_paths.update((optional_pack_path, optional_manifest_path))

    upstream_paths: dict[str, str] = {}
    for decision_id in UPSTREAM_DECISION_IDS:
        relative_path = f"decisions/{decision_id}.json"
        _write_json(
            path.parent / relative_path,
            {
                "kind": f"{decision_id}-decision-v1",
                "passed": True,
                "releaseAcceptance": True,
                "sourceRevision": revision,
            },
        )
        upstream_paths[decision_id] = relative_path
        retained_paths.add(relative_path)

    public_install_results: list[dict[str, object]] = []
    for platform in ARTIFACT_PLATFORMS:
        relative_path = f"evidence/public-install-{platform}.json"
        _write_bytes(path.parent / relative_path)
        retained_paths.add(relative_path)
        public_install_results.append(
            {
                "platformId": platform,
                "result": "pass",
                "installedArtifactSha256": artifact_hashes[platform],
                "smokeStateHash": "600f29e8919a9400",
                "evidencePaths": [relative_path],
            }
        )

    preserved_paths: dict[str, list[str]] = {}
    for category in PRESERVED_EVIDENCE_CATEGORIES:
        relative_path = f"preserved/{category}.json"
        _write_bytes(path.parent / relative_path)
        retained_paths.add(relative_path)
        preserved_paths[category] = [relative_path]

    return {
        "schemaVersion": 1,
        "kind": "vibesnake-stable-promotion-record-v1",
        "sourceRevision": revision,
        "appVersion": "1.0.0",
        "tagName": "1.0.0",
        "tagObjectRevision": revision,
        "protectedWorkflowRunId": "1234567890",
        "artifactSha256ByPlatform": artifact_hashes,
        "artifactPathsByPlatform": artifact_paths,
        "manifestSha256ByPlatform": manifest_hashes,
        "manifestPathsByPlatform": manifest_paths,
        "provenanceSha256ByPlatform": provenance_hashes,
        "provenancePathsByPlatform": provenance_paths,
        "checksumPathsByPlatform": checksum_paths,
        "optionalPackSha256": hashlib.sha256((path.parent / optional_pack_path).read_bytes()).hexdigest(),
        "optionalPackPath": optional_pack_path,
        "optionalPackManifestSha256": hashlib.sha256((path.parent / optional_manifest_path).read_bytes()).hexdigest(),
        "optionalPackManifestPath": optional_manifest_path,
        "upstreamDecisionPathsById": upstream_paths,
        "publicInstallResults": public_install_results,
        "preservedEvidencePathsByCategory": preserved_paths,
        "stableContractAcknowledgements": list(STABLE_CONTRACT_ACKNOWLEDGEMENTS),
        "retainedFileSha256": {
            relative_path: hashlib.sha256((path.parent / relative_path).read_bytes()).hexdigest()
            for relative_path in sorted(retained_paths)
        },
    }


def test_repository_stable_promotion_guard_is_exact_and_pending() -> None:
    errors, evidence = validate_stable_promotion()

    assert errors == []
    assert evidence["passed"] is True
    assert evidence["guardQualified"] is True
    assert evidence["stableVersion"] == "1.0.0"
    assert evidence["stableTag"] == "1.0.0"
    assert evidence["artifactPlatformCount"] == 3
    assert evidence["upstreamDecisionCount"] == 10
    assert evidence["preservedEvidenceCategoryCount"] == 7
    assert evidence["stableContractAcknowledgementCount"] == 6
    assert evidence["recordSupplied"] is False
    assert evidence["promotionComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert len(evidence["pendingGates"]) == 6


def test_contract_rejects_a_missing_upstream_decision(tmp_path: Path) -> None:
    contract = copy.deepcopy(json.loads(CONTRACT_PATH.read_text(encoding="utf-8")))
    contract["upstreamDecisionIds"].remove("platform-signing")
    path = tmp_path / "contract.json"
    _write_json(path, contract)

    errors, _ = validate_contract(path)

    assert any("contract.upstreamDecisionIds must be" in error for error in errors)


def test_exact_protected_workflow_record_closes_stable_promotion(tmp_path: Path) -> None:
    record_path = tmp_path / "promotion" / "record.json"
    _write_json(record_path, _promotion_record(record_path))

    errors, evidence = validate_stable_promotion(
        record_path=record_path,
        expected_revision="a" * 40,
    )

    assert errors == []
    assert evidence["recordSupplied"] is True
    assert evidence["promotionComplete"] is True
    assert evidence["releaseAcceptance"] is True
    assert evidence["pendingGates"] == []


def test_failed_upstream_decision_and_tampered_public_artifact_block_promotion(tmp_path: Path) -> None:
    record_path = tmp_path / "promotion" / "record.json"
    record = _promotion_record(record_path)
    failed_decision_path = record_path.parent / record["upstreamDecisionPathsById"]["human-playtest"]
    failed_decision = json.loads(failed_decision_path.read_text(encoding="utf-8"))
    failed_decision["releaseAcceptance"] = False
    _write_json(failed_decision_path, failed_decision)
    artifact_path = record_path.parent / record["artifactPathsByPlatform"]["windows-x64"]
    artifact_path.write_bytes(b"tampered public artifact")
    _write_json(record_path, record)

    errors, evidence = validate_stable_promotion(
        record_path=record_path,
        expected_revision="a" * 40,
    )

    assert evidence["promotionComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert "upstream decision human-playtest did not accept release" in errors
    assert "promotion artifact hash mismatch for windows-x64" in errors
    assert any("stable promotion retained file hash mismatch" in error for error in errors)
