"""Validate the V090-10 staged release and rollback rehearsal."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config" / "release_rehearsal_v1.json"
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
VERSION_PATTERN = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?")
UTC_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")
ROLE_PATTERN = re.compile(r"[a-z0-9][a-z0-9-]{2,63}")
ARTIFACT_PLATFORMS = ("windows-x64", "macos-universal", "linux-x64")
PLATFORM_OPERATION_IDS = (
    "download",
    "checksum",
    "signature-verification",
    "install",
    "launch",
    "save-creation",
    "optional-content-install",
    "optional-content-removal",
    "update",
    "rollback",
    "application-removal",
)
AUTHORITY_OPERATION_IDS = ("publish", "halt", "replace", "communicate")
RESULT_VALUES = ("pass", "fail", "blocked")
RECORD_FIELDS = (
    "schemaVersion",
    "kind",
    "rehearsalId",
    "sourceRevision",
    "appVersion",
    "previousVersion",
    "stagedLocationId",
    "executedUtc",
    "candidateArtifactSha256ByPlatform",
    "candidateArtifactPathsByPlatform",
    "previousArtifactSha256ByPlatform",
    "previousArtifactPathsByPlatform",
    "candidateManifestSha256ByPlatform",
    "candidateManifestPathsByPlatform",
    "releaseMaterialsDecisionSha256",
    "releaseMaterialsDecisionPath",
    "migrationFixtureSetSha256",
    "migrationFixturePaths",
    "platformResults",
    "withdrawalResult",
    "authorityRecords",
    "retainedFileSha256",
)
PLATFORM_RESULT_FIELDS = (
    "platformId",
    "operationResults",
    "evidencePathsByOperation",
    "protectedUserDataSha256Before",
    "protectedUserDataSha256After",
)
WITHDRAWAL_FIELDS = (
    "candidateUnavailable",
    "previousArtifactRestored",
    "userDataPreserved",
    "communicationPrepared",
    "evidencePaths",
)
AUTHORITY_FIELDS = ("operationId", "roleId", "authorizationVerified", "evidencePaths")
PREREQUISITE_PATHS = (
    "config/release_materials_v1.json",
    "config/release_signing_policy.json",
    "docs/release/PACKAGING.md",
    "docs/release/SIGNING.md",
    "docs/guides/RECOVERY.md",
)
RELEASE_RULES = (
    "The staged candidate artifacts, manifests, release-material decision, previous artifacts, and migration fixtures are retained and hash-verified.",
    "Download, checksum, signature, install, launch, save creation, optional content, update, rollback, and removal pass on every platform.",
    "Rollback and application removal preserve the protected preexisting user-data fixture exactly.",
    "Withdrawal makes the candidate unavailable, restores the previous artifact, preserves user data, and prepares communication.",
    "Publish, halt, replace, and communicate authority is assigned to verified operational roles without storing personal data.",
    "Any failed or blocked operation prevents rehearsal acceptance.",
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


def _nonempty_string(value: Any, label: str, errors: list[str]) -> bool:
    valid = isinstance(value, str) and bool(value.strip())
    if not valid:
        errors.append(f"{label} must be a nonempty string")
    return valid


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


def _file_map(value: Any, base: Path, label: str, errors: list[str]) -> dict[str, str]:
    if not _strict_keys(value, set(ARTIFACT_PLATFORMS), label, errors):
        return {}
    paths: dict[str, str] = {}
    for platform, relative_path in value.items():
        if not _safe_relative_path(relative_path):
            errors.append(f"{label}.{platform} must be a safe relative path")
            continue
        path = base / relative_path
        if not path.is_file() or path.stat().st_size == 0:
            errors.append(f"{label}.{platform} is missing or empty: {relative_path}")
            continue
        paths[platform] = relative_path
    return paths


def _digest_map(value: Any, label: str, errors: list[str]) -> dict[str, str]:
    if not _strict_keys(value, set(ARTIFACT_PLATFORMS), label, errors):
        return {}
    digests: dict[str, str] = {}
    for platform, digest in value.items():
        if not SHA256_PATTERN.fullmatch(str(digest)):
            errors.append(f"{label}.{platform} must be a SHA-256 digest")
        else:
            digests[platform] = str(digest)
    return digests


def _canonical_app_version(errors: list[str]) -> str | None:
    version_path = ROOT / "VERSION"
    if not version_path.is_file():
        errors.append(f"missing canonical product version: {version_path}")
        return None
    version = version_path.read_text(encoding="utf-8").strip()
    if not VERSION_PATTERN.fullmatch(version):
        errors.append("could not resolve the canonical application version")
        return None
    return version


def validate_contract(contract_path: Path = CONTRACT_PATH) -> tuple[list[str], dict[str, Any] | None]:
    """Validate the exact release-rehearsal contract."""
    errors: list[str] = []
    contract = _read_json(contract_path, "release rehearsal contract", errors)
    expected_fields = {
        "schemaVersion",
        "kind",
        "status",
        "artifactPlatforms",
        "platformOperationIds",
        "authorityOperationIds",
        "resultValues",
        "requiredRecordFields",
        "requiredPlatformResultFields",
        "requiredWithdrawalFields",
        "requiredAuthorityFields",
        "prerequisitePaths",
        "releaseRules",
    }
    if not _strict_keys(contract, expected_fields, "contract", errors):
        return errors, contract if isinstance(contract, dict) else None
    _exact(contract["schemaVersion"], 1, "contract.schemaVersion", errors)
    _exact(contract["kind"], "vibesnake-release-rehearsal-v1", "contract.kind", errors)
    _exact(contract["status"], "qualified-handoff-execution-pending", "contract.status", errors)
    exact_lists = (
        ("artifactPlatforms", ARTIFACT_PLATFORMS),
        ("platformOperationIds", PLATFORM_OPERATION_IDS),
        ("authorityOperationIds", AUTHORITY_OPERATION_IDS),
        ("resultValues", RESULT_VALUES),
        ("requiredRecordFields", RECORD_FIELDS),
        ("requiredPlatformResultFields", PLATFORM_RESULT_FIELDS),
        ("requiredWithdrawalFields", WITHDRAWAL_FIELDS),
        ("requiredAuthorityFields", AUTHORITY_FIELDS),
        ("prerequisitePaths", PREREQUISITE_PATHS),
        ("releaseRules", RELEASE_RULES),
    )
    for field, expected in exact_lists:
        _exact(contract[field], list(expected), f"contract.{field}", errors)
    return errors, contract


def _fixture_set_sha(paths: list[str], base: Path) -> str:
    digest = hashlib.sha256()
    for relative_path in sorted(paths):
        digest.update(relative_path.encode("utf-8"))
        digest.update(b"\0")
        digest.update(hashlib.sha256((base / relative_path).read_bytes()).digest())
    return digest.hexdigest()


def _validate_record(
    path: Path,
    expected_revision: str,
    errors: list[str],
) -> dict[str, Any] | None:
    record = _read_json(path, "release rehearsal record", errors)
    if not _strict_keys(record, set(RECORD_FIELDS), "rehearsal", errors):
        return None
    _exact(record["schemaVersion"], 1, "rehearsal.schemaVersion", errors)
    _exact(record["kind"], "vibesnake-release-rehearsal-record-v1", "rehearsal.kind", errors)
    _nonempty_string(record["rehearsalId"], "rehearsal.rehearsalId", errors)
    if not REVISION_PATTERN.fullmatch(str(record["sourceRevision"])):
        errors.append("rehearsal.sourceRevision must be a lowercase 40-character revision")
    _exact(record["sourceRevision"], expected_revision, "rehearsal.sourceRevision", errors)
    canonical_version = _canonical_app_version(errors)
    if canonical_version is not None:
        _exact(record["appVersion"], canonical_version, "rehearsal.appVersion", errors)
    if not VERSION_PATTERN.fullmatch(str(record["previousVersion"])):
        errors.append("rehearsal.previousVersion must be a semantic version")
    if record["previousVersion"] == record["appVersion"]:
        errors.append("rehearsal.previousVersion must differ from appVersion")
    _nonempty_string(record["stagedLocationId"], "rehearsal.stagedLocationId", errors)
    if not UTC_PATTERN.fullmatch(str(record["executedUtc"])):
        errors.append("rehearsal.executedUtc must use YYYY-MM-DDTHH:MM:SSZ")

    base = path.parent
    candidate_digests = _digest_map(record["candidateArtifactSha256ByPlatform"], "candidate artifact digests", errors)
    candidate_paths = _file_map(record["candidateArtifactPathsByPlatform"], base, "candidate artifact paths", errors)
    previous_digests = _digest_map(record["previousArtifactSha256ByPlatform"], "previous artifact digests", errors)
    previous_paths = _file_map(record["previousArtifactPathsByPlatform"], base, "previous artifact paths", errors)
    manifest_digests = _digest_map(record["candidateManifestSha256ByPlatform"], "candidate manifest digests", errors)
    manifest_paths = _file_map(record["candidateManifestPathsByPlatform"], base, "candidate manifest paths", errors)
    referenced_paths = set(candidate_paths.values()) | set(previous_paths.values()) | set(manifest_paths.values())
    for platform in ARTIFACT_PLATFORMS:
        for digests, paths, label in (
            (candidate_digests, candidate_paths, "candidate artifact"),
            (previous_digests, previous_paths, "previous artifact"),
            (manifest_digests, manifest_paths, "candidate manifest"),
        ):
            if platform in digests and platform in paths:
                actual = hashlib.sha256((base / paths[platform]).read_bytes()).hexdigest()
                if actual != digests[platform]:
                    errors.append(f"{label} hash mismatch for {platform}")
        if candidate_digests.get(platform) == previous_digests.get(platform):
            errors.append(f"candidate and previous artifact hashes must differ for {platform}")

    decision_path = record["releaseMaterialsDecisionPath"]
    decision_paths = _existing_paths([decision_path], base, "release materials decision path", errors)
    referenced_paths.update(decision_paths)
    if not SHA256_PATTERN.fullmatch(str(record["releaseMaterialsDecisionSha256"])):
        errors.append("releaseMaterialsDecisionSha256 must be a SHA-256 digest")
    elif decision_paths:
        actual = hashlib.sha256((base / decision_path).read_bytes()).hexdigest()
        if actual != record["releaseMaterialsDecisionSha256"]:
            errors.append("release materials decision hash mismatch")

    fixture_paths = _existing_paths(record["migrationFixturePaths"], base, "migration fixture paths", errors)
    referenced_paths.update(fixture_paths)
    if not SHA256_PATTERN.fullmatch(str(record["migrationFixtureSetSha256"])):
        errors.append("migrationFixtureSetSha256 must be a SHA-256 digest")
    elif fixture_paths and _fixture_set_sha(fixture_paths, base) != record["migrationFixtureSetSha256"]:
        errors.append("migration fixture set hash mismatch")

    results = record["platformResults"]
    if not isinstance(results, list) or len(results) != len(ARTIFACT_PLATFORMS):
        errors.append("platformResults must contain exactly three rows")
    else:
        seen_platforms: set[str] = set()
        for index, result in enumerate(results):
            label = f"platformResults[{index}]"
            if not _strict_keys(result, set(PLATFORM_RESULT_FIELDS), label, errors):
                continue
            platform = result["platformId"] if isinstance(result["platformId"], str) else ""
            if platform not in ARTIFACT_PLATFORMS or platform in seen_platforms:
                errors.append(f"{label}.platformId must be unique and supported")
            seen_platforms.add(platform)
            operations = result["operationResults"]
            if _strict_keys(operations, set(PLATFORM_OPERATION_IDS), f"{label}.operationResults", errors):
                for operation_id, value in operations.items():
                    if value not in RESULT_VALUES:
                        errors.append(f"{label}.operationResults.{operation_id} is unsupported")
                    elif value != "pass":
                        errors.append(f"{label}.operationResults.{operation_id} blocks rehearsal")
            evidence_paths = result["evidencePathsByOperation"]
            if _strict_keys(
                evidence_paths,
                set(PLATFORM_OPERATION_IDS),
                f"{label}.evidencePathsByOperation",
                errors,
            ):
                for operation_id in PLATFORM_OPERATION_IDS:
                    values = _existing_paths(
                        evidence_paths[operation_id],
                        base,
                        f"{label}.evidencePathsByOperation.{operation_id}",
                        errors,
                    )
                    referenced_paths.update(values)
            before = result["protectedUserDataSha256Before"]
            after = result["protectedUserDataSha256After"]
            if not SHA256_PATTERN.fullmatch(str(before)) or not SHA256_PATTERN.fullmatch(str(after)):
                errors.append(f"{label} protected user-data values must be SHA-256 digests")
            elif before != after:
                errors.append(f"{label} rollback or removal changed protected user data")
        if seen_platforms != set(ARTIFACT_PLATFORMS):
            errors.append("platformResults must cover every artifact platform")

    withdrawal = record["withdrawalResult"]
    if _strict_keys(withdrawal, set(WITHDRAWAL_FIELDS), "withdrawalResult", errors):
        for field in (
            "candidateUnavailable",
            "previousArtifactRestored",
            "userDataPreserved",
            "communicationPrepared",
        ):
            _exact(withdrawal[field], True, f"withdrawalResult.{field}", errors)
        values = _existing_paths(withdrawal["evidencePaths"], base, "withdrawalResult.evidencePaths", errors)
        referenced_paths.update(values)

    authorities = record["authorityRecords"]
    if not isinstance(authorities, list) or len(authorities) != len(AUTHORITY_OPERATION_IDS):
        errors.append("authorityRecords must contain exactly four rows")
    else:
        seen_operations: set[str] = set()
        for index, authority in enumerate(authorities):
            label = f"authorityRecords[{index}]"
            if not _strict_keys(authority, set(AUTHORITY_FIELDS), label, errors):
                continue
            operation_id = authority["operationId"] if isinstance(authority["operationId"], str) else ""
            if operation_id not in AUTHORITY_OPERATION_IDS or operation_id in seen_operations:
                errors.append(f"{label}.operationId must be unique and supported")
            seen_operations.add(operation_id)
            if not ROLE_PATTERN.fullmatch(str(authority["roleId"])):
                errors.append(f"{label}.roleId must be a non-personal operational role ID")
            _exact(authority["authorizationVerified"], True, f"{label}.authorizationVerified", errors)
            values = _existing_paths(authority["evidencePaths"], base, f"{label}.evidencePaths", errors)
            referenced_paths.update(values)
        if seen_operations != set(AUTHORITY_OPERATION_IDS):
            errors.append("authorityRecords must cover every authority operation")

    retained_hashes = record["retainedFileSha256"]
    _strict_keys(retained_hashes, referenced_paths, "retainedFileSha256", errors)
    if isinstance(retained_hashes, dict):
        for relative_path, expected_sha in retained_hashes.items():
            if not _safe_relative_path(relative_path):
                errors.append(f"retainedFileSha256 contains an unsafe path: {relative_path}")
                continue
            retained_path = base / relative_path
            if not retained_path.is_file():
                continue
            actual_sha = hashlib.sha256(retained_path.read_bytes()).hexdigest()
            if not SHA256_PATTERN.fullmatch(str(expected_sha)) or actual_sha != expected_sha:
                errors.append(f"retained rehearsal file hash mismatch: {relative_path}")
    return record


def validate_release_rehearsal(
    contract_path: Path = CONTRACT_PATH,
    record_path: Path | None = None,
    expected_revision: str | None = None,
) -> tuple[list[str], dict[str, Any]]:
    """Validate the handoff and optional retained rehearsal record."""
    contract_errors, contract = validate_contract(contract_path)
    errors = list(contract_errors)
    prerequisite_hashes: dict[str, str] = {}
    for relative_path in PREREQUISITE_PATHS:
        path = ROOT / relative_path
        if not path.is_file():
            errors.append(f"missing rehearsal prerequisite: {relative_path}")
        else:
            prerequisite_hashes[relative_path] = hashlib.sha256(path.read_bytes()).hexdigest()
    if record_path is not None and not REVISION_PATTERN.fullmatch(str(expected_revision)):
        errors.append("an exact lowercase 40-character expected revision is required with a rehearsal record")
    record = (
        _validate_record(record_path, str(expected_revision), errors)
        if record_path is not None and REVISION_PATTERN.fullmatch(str(expected_revision))
        else None
    )
    rehearsal_complete = record_path is not None and record is not None and not errors
    evidence = {
        "schemaVersion": 1,
        "kind": "release-rehearsal-handoff-v1",
        "passed": not errors,
        "protocolQualified": not contract_errors and len(prerequisite_hashes) == len(PREREQUISITE_PATHS),
        "contractSha256": hashlib.sha256(contract_path.read_bytes()).hexdigest() if contract_path.is_file() else None,
        "prerequisiteSha256": prerequisite_hashes,
        "artifactPlatformCount": len(ARTIFACT_PLATFORMS),
        "platformOperationCount": len(PLATFORM_OPERATION_IDS),
        "requiredPlatformOperationCellCount": len(ARTIFACT_PLATFORMS) * len(PLATFORM_OPERATION_IDS),
        "authorityOperationCount": len(AUTHORITY_OPERATION_IDS),
        "recordSupplied": record_path is not None,
        "rehearsalComplete": rehearsal_complete,
        "releaseAcceptance": rehearsal_complete,
        "pendingGates": []
        if rehearsal_complete
        else [
            "staged-final-artifacts-and-checksums",
            "three-platform-install-update-rollback-removal",
            "optional-content-lifecycle",
            "withdrawal-and-previous-artifact-restoration",
            "user-data-preservation",
            "verified-release-authority-roles",
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
    errors, evidence = validate_release_rehearsal(
        args.contract.resolve(),
        args.record.resolve() if args.record is not None else None,
        args.expected_revision,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if errors:
        print("Release rehearsal qualification failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    if args.record is None:
        print("Release rehearsal handoff qualified; staged execution remains pending.")
    else:
        print("Release and rollback rehearsal accepted for the exact candidate.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
