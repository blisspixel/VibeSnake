"""Validate the final 1.0 protected-promotion record without performing release actions."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config" / "stable_promotion_v1.json"
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
STATE_HASH_PATTERN = re.compile(r"[0-9a-f]{16}")
WORKFLOW_RUN_PATTERN = re.compile(r"[1-9][0-9]{5,19}")
STABLE_VERSION = "1.0.0"
STABLE_TAG = "1.0.0"
ARTIFACT_PLATFORMS = ("windows-x64", "macos-universal", "linux-x64")
UPSTREAM_DECISION_IDS = (
    "release-matrix",
    "manual-product-matrix",
    "external-validation",
    "release-materials",
    "release-rehearsal",
    "content-approval",
    "hardware-performance",
    "accessibility-human-review",
    "human-playtest",
    "platform-signing",
)
PRESERVED_EVIDENCE_CATEGORIES = (
    "build-logs",
    "manifests",
    "sbom",
    "checksums",
    "migration-fixtures",
    "previous-artifacts",
    "support-record",
)
STABLE_CONTRACT_ACKNOWLEDGEMENTS = (
    "patch-releases-preserve-scored-rules-unless-a-disclosed-correctness-or-exploit-fix-requires-change",
    "save-migrations-remain-nondestructive-and-tested",
    "existing-score-categories-retain-rules-identity",
    "removed-content-remains-visible-as-missing-or-incompatible",
    "accessibility-support-is-regression-tested",
    "offline-core-play-requires-no-account-or-network",
)
RECORD_FIELDS = (
    "schemaVersion",
    "kind",
    "sourceRevision",
    "appVersion",
    "tagName",
    "tagObjectRevision",
    "protectedWorkflowRunId",
    "artifactSha256ByPlatform",
    "artifactPathsByPlatform",
    "manifestSha256ByPlatform",
    "manifestPathsByPlatform",
    "provenanceSha256ByPlatform",
    "provenancePathsByPlatform",
    "checksumPathsByPlatform",
    "optionalPackSha256",
    "optionalPackPath",
    "optionalPackManifestSha256",
    "optionalPackManifestPath",
    "upstreamDecisionPathsById",
    "publicInstallResults",
    "preservedEvidencePathsByCategory",
    "stableContractAcknowledgements",
    "retainedFileSha256",
)
PUBLIC_INSTALL_FIELDS = (
    "platformId",
    "result",
    "installedArtifactSha256",
    "smokeStateHash",
    "evidencePaths",
)
RELEASE_RULES = (
    "The protected workflow rebuilds from tag 1.0.0 at the exact reviewed source revision.",
    "All ten upstream decisions pass and explicitly accept release for the same source revision.",
    "All three public artifacts, manifests, provenance bundles, and checksum files are retained and hash-verified.",
    "The exact approved optional pack and manifest are retained and hash-verified separately from the core player.",
    "One public-file install and deterministic smoke passes on every platform using the published artifact bytes.",
    "Build logs, manifests, SBOM, checksums, migration fixtures, previous artifacts, and support records are preserved.",
    "The stable compatibility contract is acknowledged exactly and cannot be weakened during promotion.",
    "Renaming, copying, or manually uploading qualification artifacts cannot satisfy stable promotion.",
)


def _read_json(path: Path, label: str, errors: list[str]) -> Any | None:
    if not path.is_file():
        errors.append(f"missing {label}: {path}")
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"unreadable {label}: {path}: {exc}")
        return None


def _strict_keys(value: Any, expected: set[str], label: str, errors: list[str]) -> bool:
    if not isinstance(value, dict):
        errors.append(f"{label} must be an object")
        return False
    if set(value) != expected:
        errors.append(f"{label} fields must be {sorted(expected)!r}; got {sorted(value)!r}")
        return False
    return True


def _exact(value: Any, expected: Any, label: str, errors: list[str]) -> None:
    if value != expected:
        errors.append(f"{label} must be {expected!r}; got {value!r}")


def _safe_relative_path(value: Any) -> bool:
    if not isinstance(value, str) or not value or "\\" in value:
        return False
    path = Path(value)
    return not path.is_absolute() and ".." not in path.parts


def _existing_paths(value: Any, base: Path, label: str, errors: list[str]) -> list[str]:
    if (
        not isinstance(value, list)
        or not value
        or not all(isinstance(item, str) for item in value)
        or len(value) != len(set(value))
        or not all(_safe_relative_path(item) for item in value)
    ):
        errors.append(f"{label} must contain unique safe relative paths")
        return []
    missing = [item for item in value if not (base / item).is_file() or (base / item).stat().st_size == 0]
    if missing:
        errors.append(f"{label} reference missing or empty retained files: {', '.join(missing)}")
        return []
    return list(value)


def _strict_path_map(
    value: Any,
    keys: tuple[str, ...],
    base: Path,
    label: str,
    errors: list[str],
) -> dict[str, str]:
    if not _strict_keys(value, set(keys), label, errors):
        return {}
    result: dict[str, str] = {}
    for key, relative_path in value.items():
        if not _safe_relative_path(relative_path):
            errors.append(f"{label}.{key} must be a safe relative path")
            continue
        path = base / relative_path
        if not path.is_file() or path.stat().st_size == 0:
            errors.append(f"{label}.{key} is missing or empty: {relative_path}")
            continue
        result[key] = relative_path
    return result


def _strict_digest_map(
    value: Any,
    keys: tuple[str, ...],
    label: str,
    errors: list[str],
) -> dict[str, str]:
    if not _strict_keys(value, set(keys), label, errors):
        return {}
    result: dict[str, str] = {}
    for key, digest in value.items():
        if not SHA256_PATTERN.fullmatch(str(digest)):
            errors.append(f"{label}.{key} must be a SHA-256 digest")
        else:
            result[key] = str(digest)
    return result


def _verify_digest_pairs(
    digests: dict[str, str],
    paths: dict[str, str],
    base: Path,
    label: str,
    errors: list[str],
) -> None:
    for key in digests.keys() & paths.keys():
        actual = hashlib.sha256((base / paths[key]).read_bytes()).hexdigest()
        if actual != digests[key]:
            errors.append(f"{label} hash mismatch for {key}")


def validate_contract(contract_path: Path = CONTRACT_PATH) -> tuple[list[str], dict[str, Any] | None]:
    """Validate the exact stable-promotion contract."""
    errors: list[str] = []
    contract = _read_json(contract_path, "stable promotion contract", errors)
    expected_fields = {
        "schemaVersion",
        "kind",
        "status",
        "stableVersion",
        "stableTag",
        "artifactPlatforms",
        "upstreamDecisionIds",
        "preservedEvidenceCategories",
        "stableContractAcknowledgements",
        "requiredRecordFields",
        "requiredPublicInstallFields",
        "releaseRules",
    }
    if not _strict_keys(contract, expected_fields, "contract", errors):
        return errors, contract if isinstance(contract, dict) else None
    _exact(contract["schemaVersion"], 1, "contract.schemaVersion", errors)
    _exact(contract["kind"], "vibesnake-stable-promotion-v1", "contract.kind", errors)
    _exact(contract["status"], "guard-qualified-promotion-pending", "contract.status", errors)
    _exact(contract["stableVersion"], STABLE_VERSION, "contract.stableVersion", errors)
    _exact(contract["stableTag"], STABLE_TAG, "contract.stableTag", errors)
    exact_lists = (
        ("artifactPlatforms", ARTIFACT_PLATFORMS),
        ("upstreamDecisionIds", UPSTREAM_DECISION_IDS),
        ("preservedEvidenceCategories", PRESERVED_EVIDENCE_CATEGORIES),
        ("stableContractAcknowledgements", STABLE_CONTRACT_ACKNOWLEDGEMENTS),
        ("requiredRecordFields", RECORD_FIELDS),
        ("requiredPublicInstallFields", PUBLIC_INSTALL_FIELDS),
        ("releaseRules", RELEASE_RULES),
    )
    for field, expected in exact_lists:
        _exact(contract[field], list(expected), f"contract.{field}", errors)
    return errors, contract


def _validate_upstream_decision(
    path: Path,
    decision_id: str,
    expected_revision: str,
    errors: list[str],
) -> None:
    decision = _read_json(path, f"upstream decision {decision_id}", errors)
    if not isinstance(decision, dict):
        return
    if decision.get("passed") is not True:
        errors.append(f"upstream decision {decision_id} did not pass")
    if decision.get("releaseAcceptance") is not True:
        errors.append(f"upstream decision {decision_id} did not accept release")
    if decision.get("sourceRevision") != expected_revision:
        errors.append(f"upstream decision {decision_id} source revision does not match")


def _validate_record(path: Path, expected_revision: str, errors: list[str]) -> dict[str, Any] | None:
    record = _read_json(path, "stable promotion record", errors)
    if not _strict_keys(record, set(RECORD_FIELDS), "promotion", errors):
        return None
    _exact(record["schemaVersion"], 1, "promotion.schemaVersion", errors)
    _exact(record["kind"], "vibesnake-stable-promotion-record-v1", "promotion.kind", errors)
    _exact(record["sourceRevision"], expected_revision, "promotion.sourceRevision", errors)
    _exact(record["appVersion"], STABLE_VERSION, "promotion.appVersion", errors)
    _exact(record["tagName"], STABLE_TAG, "promotion.tagName", errors)
    _exact(record["tagObjectRevision"], expected_revision, "promotion.tagObjectRevision", errors)
    if not WORKFLOW_RUN_PATTERN.fullmatch(str(record["protectedWorkflowRunId"])):
        errors.append("promotion.protectedWorkflowRunId must be a retained numeric workflow run ID")

    base = path.parent
    artifact_digests = _strict_digest_map(
        record["artifactSha256ByPlatform"], ARTIFACT_PLATFORMS, "promotion artifact digests", errors
    )
    artifact_paths = _strict_path_map(
        record["artifactPathsByPlatform"], ARTIFACT_PLATFORMS, base, "promotion artifact paths", errors
    )
    manifest_digests = _strict_digest_map(
        record["manifestSha256ByPlatform"], ARTIFACT_PLATFORMS, "promotion manifest digests", errors
    )
    manifest_paths = _strict_path_map(
        record["manifestPathsByPlatform"], ARTIFACT_PLATFORMS, base, "promotion manifest paths", errors
    )
    provenance_digests = _strict_digest_map(
        record["provenanceSha256ByPlatform"], ARTIFACT_PLATFORMS, "promotion provenance digests", errors
    )
    provenance_paths = _strict_path_map(
        record["provenancePathsByPlatform"], ARTIFACT_PLATFORMS, base, "promotion provenance paths", errors
    )
    checksum_paths = _strict_path_map(
        record["checksumPathsByPlatform"], ARTIFACT_PLATFORMS, base, "promotion checksum paths", errors
    )
    _verify_digest_pairs(artifact_digests, artifact_paths, base, "promotion artifact", errors)
    _verify_digest_pairs(manifest_digests, manifest_paths, base, "promotion manifest", errors)
    _verify_digest_pairs(provenance_digests, provenance_paths, base, "promotion provenance", errors)
    referenced_paths = (
        set(artifact_paths.values())
        | set(manifest_paths.values())
        | set(provenance_paths.values())
        | set(checksum_paths.values())
    )
    for platform in ARTIFACT_PLATFORMS:
        if platform not in checksum_paths or platform not in artifact_digests:
            continue
        checksum_text = (base / checksum_paths[platform]).read_text(encoding="utf-8")
        if artifact_digests[platform] not in checksum_text:
            errors.append(f"published checksum file does not contain the artifact digest for {platform}")

    optional_paths = _existing_paths([record["optionalPackPath"]], base, "promotion.optionalPackPath", errors)
    optional_manifest_paths = _existing_paths(
        [record["optionalPackManifestPath"]], base, "promotion.optionalPackManifestPath", errors
    )
    referenced_paths.update(optional_paths)
    referenced_paths.update(optional_manifest_paths)
    for digest_field, paths, label in (
        ("optionalPackSha256", optional_paths, "optional pack"),
        ("optionalPackManifestSha256", optional_manifest_paths, "optional pack manifest"),
    ):
        expected_sha = record[digest_field]
        if not SHA256_PATTERN.fullmatch(str(expected_sha)):
            errors.append(f"promotion.{digest_field} must be a SHA-256 digest")
        elif paths and hashlib.sha256((base / paths[0]).read_bytes()).hexdigest() != expected_sha:
            errors.append(f"promotion {label} hash mismatch")

    upstream_paths = _strict_path_map(
        record["upstreamDecisionPathsById"],
        UPSTREAM_DECISION_IDS,
        base,
        "promotion upstream decisions",
        errors,
    )
    referenced_paths.update(upstream_paths.values())
    for decision_id, relative_path in upstream_paths.items():
        _validate_upstream_decision(base / relative_path, decision_id, expected_revision, errors)

    install_results = record["publicInstallResults"]
    if not isinstance(install_results, list) or len(install_results) != len(ARTIFACT_PLATFORMS):
        errors.append("promotion.publicInstallResults must contain exactly three rows")
    else:
        seen_platforms: set[str] = set()
        for index, result in enumerate(install_results):
            label = f"promotion.publicInstallResults[{index}]"
            if not _strict_keys(result, set(PUBLIC_INSTALL_FIELDS), label, errors):
                continue
            platform = result["platformId"] if isinstance(result["platformId"], str) else ""
            if platform not in ARTIFACT_PLATFORMS or platform in seen_platforms:
                errors.append(f"{label}.platformId must be unique and supported")
            seen_platforms.add(platform)
            _exact(result["result"], "pass", f"{label}.result", errors)
            _exact(
                result["installedArtifactSha256"],
                artifact_digests.get(platform),
                f"{label}.installedArtifactSha256",
                errors,
            )
            if not STATE_HASH_PATTERN.fullmatch(str(result["smokeStateHash"])):
                errors.append(f"{label}.smokeStateHash must be a 16-character state hash")
            values = _existing_paths(result["evidencePaths"], base, f"{label}.evidencePaths", errors)
            referenced_paths.update(values)
        if seen_platforms != set(ARTIFACT_PLATFORMS):
            errors.append("promotion.publicInstallResults must cover every platform")

    preserved_paths = record["preservedEvidencePathsByCategory"]
    if _strict_keys(
        preserved_paths,
        set(PRESERVED_EVIDENCE_CATEGORIES),
        "promotion preserved evidence",
        errors,
    ):
        for category in PRESERVED_EVIDENCE_CATEGORIES:
            values = _existing_paths(
                preserved_paths[category], base, f"promotion preserved evidence.{category}", errors
            )
            referenced_paths.update(values)
    _exact(
        record["stableContractAcknowledgements"],
        list(STABLE_CONTRACT_ACKNOWLEDGEMENTS),
        "promotion.stableContractAcknowledgements",
        errors,
    )
    retained_hashes = record["retainedFileSha256"]
    _strict_keys(retained_hashes, referenced_paths, "promotion.retainedFileSha256", errors)
    if isinstance(retained_hashes, dict):
        for relative_path, expected_sha in retained_hashes.items():
            if not _safe_relative_path(relative_path):
                errors.append(f"promotion.retainedFileSha256 contains an unsafe path: {relative_path}")
                continue
            retained_path = base / relative_path
            if not retained_path.is_file():
                continue
            actual_sha = hashlib.sha256(retained_path.read_bytes()).hexdigest()
            if not SHA256_PATTERN.fullmatch(str(expected_sha)) or actual_sha != expected_sha:
                errors.append(f"stable promotion retained file hash mismatch: {relative_path}")
    return record


def validate_stable_promotion(
    contract_path: Path = CONTRACT_PATH,
    record_path: Path | None = None,
    expected_revision: str | None = None,
) -> tuple[list[str], dict[str, Any]]:
    """Validate the promotion guard and optional final protected-workflow record."""
    contract_errors, contract = validate_contract(contract_path)
    errors = list(contract_errors)
    if record_path is not None and not REVISION_PATTERN.fullmatch(str(expected_revision)):
        errors.append("an exact lowercase 40-character expected revision is required with a promotion record")
    record = (
        _validate_record(record_path, str(expected_revision), errors)
        if record_path is not None and REVISION_PATTERN.fullmatch(str(expected_revision))
        else None
    )
    promotion_complete = record_path is not None and record is not None and not errors
    evidence = {
        "schemaVersion": 1,
        "kind": "stable-promotion-handoff-v1",
        "passed": not errors,
        "guardQualified": not contract_errors,
        "contractSha256": hashlib.sha256(contract_path.read_bytes()).hexdigest() if contract_path.is_file() else None,
        "stableVersion": STABLE_VERSION,
        "stableTag": STABLE_TAG,
        "artifactPlatformCount": len(ARTIFACT_PLATFORMS),
        "upstreamDecisionCount": len(UPSTREAM_DECISION_IDS),
        "preservedEvidenceCategoryCount": len(PRESERVED_EVIDENCE_CATEGORIES),
        "stableContractAcknowledgementCount": len(STABLE_CONTRACT_ACKNOWLEDGEMENTS),
        "recordSupplied": record_path is not None,
        "promotionComplete": promotion_complete,
        "releaseAcceptance": promotion_complete,
        "pendingGates": []
        if promotion_complete
        else [
            "all-upstream-release-decisions",
            "protected-1.0.0-tag-rebuild",
            "signed-attested-public-artifacts",
            "approved-optional-pack",
            "public-file-three-platform-install",
            "complete-preserved-release-record",
        ],
        "errors": errors,
    }
    return errors, evidence


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--contract", type=Path, default=CONTRACT_PATH)
    parser.add_argument("--record", type=Path)
    parser.add_argument("--expected-revision")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    errors, evidence = validate_stable_promotion(
        args.contract.resolve(),
        args.record.resolve() if args.record is not None else None,
        args.expected_revision,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if errors:
        print("Stable promotion qualification failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    if args.record is None:
        print("Stable promotion guard qualified; protected 1.0 execution remains pending.")
    else:
        print("Stable 1.0 promotion accepted for the exact protected-workflow record.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
