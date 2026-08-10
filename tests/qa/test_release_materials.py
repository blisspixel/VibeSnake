"""Contracts for the V090-09 release-material handoff."""

from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path

from scripts.check_release_materials import (
    ARTIFACT_PLATFORMS,
    CONTRACT_PATH,
    INPUT_DEVICE_IDS,
    MARKETING_CLAIM_IDS,
    OFFLINE_BEHAVIOR_VALUE,
    REQUIRED_DOCUMENT_PATHS,
    SCREENSHOT_ROLES,
    VIDEO_ROLES,
    validate_contract,
    validate_release_materials,
)


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def _write_bytes(path: Path, value: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(value)


def _write_final_documents(root: Path) -> dict[str, str]:
    root.mkdir(parents=True, exist_ok=True)
    (root / "pyproject.toml").write_text('[project]\nversion = "1.0.0"\n', encoding="utf-8")
    hashes: dict[str, str] = {}
    for index, relative_path in enumerate(REQUIRED_DOCUMENT_PATHS):
        path = root / relative_path
        contents = (
            f"# Final release document {index}\n\n"
            "Verified final candidate information, supported behavior, evidence, and player guidance. " * 4
        )
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents, encoding="utf-8")
        hashes[relative_path] = hashlib.sha256(path.read_bytes()).hexdigest()
    return hashes


def _candidate(path: Path, document_hashes: dict[str, str]) -> dict[str, object]:
    input_paths = {device_id: [f"evidence/input/{device_id}.json"] for device_id in INPUT_DEVICE_IDS}
    screenshot_paths = {role: [f"media/{role}.png"] for role in SCREENSHOT_ROLES}
    video_paths = {role: [f"media/{role}.mp4"] for role in VIDEO_ROLES}
    claim_paths = {claim_id: [f"evidence/claims/{claim_id}.json"] for claim_id in MARKETING_CLAIM_IDS}
    for paths in input_paths.values():
        _write_bytes(path.parent / paths[0], b"retained physical input evidence")
    for paths in screenshot_paths.values():
        _write_bytes(path.parent / paths[0], b"\x89PNG\r\n\x1a\n" + b"candidate image" * 8)
    for paths in video_paths.values():
        _write_bytes(path.parent / paths[0], b"\x00\x00\x00\x18ftypisom" + b"candidate video" * 8)
    for paths in claim_paths.values():
        _write_bytes(path.parent / paths[0], b"retained candidate claim evidence")
    retained_paths = {
        relative_path
        for mapping in (input_paths, screenshot_paths, video_paths, claim_paths)
        for paths in mapping.values()
        for relative_path in paths
    }
    return {
        "schemaVersion": 1,
        "kind": "vibesnake-release-materials-candidate-v1",
        "sourceRevision": "a" * 40,
        "appVersion": "1.0.0",
        "artifactManifestSha256ByPlatform": {
            platform: str(index + 1) * 64 for index, platform in enumerate(ARTIFACT_PLATFORMS)
        },
        "downloadBytesByPlatform": {platform: 100_000_000 + index for index, platform in enumerate(ARTIFACT_PLATFORMS)},
        "installedBytesByPlatform": {
            platform: 200_000_000 + index for index, platform in enumerate(ARTIFACT_PLATFORMS)
        },
        "supportedOperatingSystemsByPlatform": {
            "windows-x64": ["Windows qualified version"],
            "macos-universal": ["macOS qualified version"],
            "linux-x64": ["Linux qualified baseline"],
        },
        "inputDeviceIds": list(INPUT_DEVICE_IDS),
        "inputEvidencePathsByDevice": input_paths,
        "offlineBehavior": OFFLINE_BEHAVIOR_VALUE,
        "saveLocationsByPlatform": {
            "windows-x64": "Windows application-data location",
            "macos-universal": "macOS application-support location",
            "linux-x64": "Linux XDG data location",
        },
        "coreContentBytes": 50_000_000,
        "optionalContentBytes": 300_000_000,
        "documentationSha256": document_hashes,
        "screenshotPathsByRole": screenshot_paths,
        "videoPathsByRole": video_paths,
        "retainedFileSha256": {
            relative_path: hashlib.sha256((path.parent / relative_path).read_bytes()).hexdigest()
            for relative_path in sorted(retained_paths)
        },
        "marketingClaims": [
            {
                "claimId": claim_id,
                "statement": f"Verified candidate claim for {claim_id}.",
                "evidencePaths": claim_paths[claim_id],
            }
            for claim_id in MARKETING_CLAIM_IDS
        ],
    }


def test_repository_release_material_foundation_is_exact_and_pending() -> None:
    errors, evidence = validate_release_materials()

    assert errors == []
    assert evidence["passed"] is True
    assert evidence["foundationQualified"] is True
    assert evidence["requiredDocumentCount"] == 10
    assert evidence["artifactPlatformCount"] == 3
    assert evidence["inputDeviceCount"] == 4
    assert evidence["screenshotRoleCount"] == 6
    assert evidence["videoRoleCount"] == 2
    assert evidence["marketingClaimCount"] == 8
    assert evidence["candidateSupplied"] is False
    assert evidence["candidateMaterialComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert len(evidence["pendingGates"]) == 7


def test_contract_rejects_a_missing_video_role(tmp_path: Path) -> None:
    contract = copy.deepcopy(json.loads(CONTRACT_PATH.read_text(encoding="utf-8")))
    contract["videoRoles"].remove("accessibility-and-input")
    path = tmp_path / "contract.json"
    _write_json(path, contract)

    errors, _ = validate_contract(path)

    assert any("contract.videoRoles must be" in error for error in errors)


def test_exact_candidate_documents_media_and_claims_close_the_gate(tmp_path: Path) -> None:
    documents_root = tmp_path / "checkout"
    candidate_path = tmp_path / "retained" / "candidate.json"
    document_hashes = _write_final_documents(documents_root)
    _write_json(candidate_path, _candidate(candidate_path, document_hashes))

    errors, evidence = validate_release_materials(
        candidate_path=candidate_path,
        expected_revision="a" * 40,
        documents_root=documents_root,
    )

    assert errors == []
    assert evidence["candidateSupplied"] is True
    assert evidence["candidateMaterialComplete"] is True
    assert evidence["releaseAcceptance"] is True
    assert evidence["pendingGates"] == []


def test_tampered_or_missing_candidate_media_blocks_acceptance(tmp_path: Path) -> None:
    documents_root = tmp_path / "checkout"
    candidate_path = tmp_path / "retained" / "candidate.json"
    document_hashes = _write_final_documents(documents_root)
    candidate = _candidate(candidate_path, document_hashes)
    _write_json(candidate_path, candidate)
    tampered = candidate_path.parent / candidate["screenshotPathsByRole"]["main-menu"][0]
    tampered.write_bytes(b"not an image")
    missing = candidate_path.parent / candidate["videoPathsByRole"]["gameplay-overview"][0]
    missing.unlink()

    errors, evidence = validate_release_materials(
        candidate_path=candidate_path,
        expected_revision="a" * 40,
        documents_root=documents_root,
    )

    assert evidence["candidateMaterialComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert any("not a recognized retained image" in error for error in errors)
    assert any("missing or empty retained files" in error for error in errors)
    assert any("retained file hash mismatch" in error for error in errors)
