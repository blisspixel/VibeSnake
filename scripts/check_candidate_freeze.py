"""Validate the release-candidate freeze policy and its optional hash baseline."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
POLICY_PATH = ROOT / "config" / "candidate_freeze_policy_v1.json"
EXPECTED_CONTRACT_IDS = (
    "rules",
    "save-schemas",
    "replay-schema",
    "content-manifests",
    "input-defaults",
    "accessibility-defaults",
)
EXPECTED_PREREQUISITES = (
    "0.8.0-acceptance",
    "clean-revision",
    "green-ci",
    "release-matrix-ready",
)
EXPECTED_CHANGE_KINDS = (
    "defect",
    "compatibility",
    "performance",
    "documentation",
    "release-operation",
)
EXPECTED_CHANGE_EVIDENCE = (
    "changeKind",
    "failedGate",
    "severity",
    "reproduction",
    "verification",
    "affectedFrozenContracts",
    "risk",
    "rollback",
)
EXPECTED_SEVERITY_EFFECTS = {
    "P0": "always-blocks",
    "P1": "always-blocks",
    "P2": "decision-required",
    "P3": "known-issue-eligible",
}
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
UTC_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")


def _read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def _strict_keys(value: Any, expected: set[str], context: str, errors: list[str]) -> bool:
    if not isinstance(value, dict):
        errors.append(f"{context} must be an object")
        return False
    actual = set(value)
    if actual != expected:
        errors.append(f"{context} fields must be {sorted(expected)!r}; got {sorted(actual)!r}")
        return False
    return True


def _safe_pattern(pattern: Any) -> bool:
    if not isinstance(pattern, str) or not pattern or "\\" in pattern:
        return False
    path = Path(pattern)
    return not path.is_absolute() and ".." not in path.parts


def _resolve_surfaces(root: Path, contracts: Any, errors: list[str]) -> dict[str, tuple[str, ...]]:
    owners: dict[str, set[str]] = defaultdict(set)
    if not isinstance(contracts, list):
        errors.append("frozenContracts must be an array")
        return {}

    contract_ids: list[str] = []
    for index, contract in enumerate(contracts):
        context = f"frozenContracts[{index}]"
        if not _strict_keys(contract, {"id", "pathPatterns"}, context, errors):
            continue
        contract_id = contract["id"]
        patterns = contract["pathPatterns"]
        contract_ids.append(contract_id)
        if not isinstance(patterns, list) or not patterns:
            errors.append(f"{context}.pathPatterns must be a nonempty array")
            continue
        matched: set[str] = set()
        for pattern in patterns:
            if not _safe_pattern(pattern):
                errors.append(f"{context} contains an unsafe path pattern: {pattern!r}")
                continue
            matches = sorted(path for path in root.glob(pattern) if path.is_file())
            if not matches:
                errors.append(f"{context} path pattern matched no files: {pattern}")
            for path in matches:
                relative = path.relative_to(root).as_posix()
                matched.add(relative)
                owners[relative].add(contract_id)
        if not matched:
            errors.append(f"{context} resolved to no files")

    if tuple(contract_ids) != EXPECTED_CONTRACT_IDS:
        errors.append(f"frozenContracts IDs must be {list(EXPECTED_CONTRACT_IDS)!r}; got {contract_ids!r}")
    return {path: tuple(sorted(ids)) for path, ids in sorted(owners.items())}


def _validate_policy_shape(root: Path, policy: Any) -> tuple[list[str], dict[str, tuple[str, ...]]]:
    errors: list[str] = []
    expected_fields = {
        "schemaVersion",
        "policyId",
        "candidateVersion",
        "promotionVersion",
        "state",
        "activation",
        "prerequisiteGates",
        "frozenContracts",
        "allowedChangeKinds",
        "requiredChangeEvidence",
        "severityPolicy",
    }
    if not _strict_keys(policy, expected_fields, "policy", errors):
        return errors, {}

    expected_scalars = {
        "schemaVersion": 1,
        "policyId": "candidate-freeze-policy-v1",
        "candidateVersion": "0.9.0",
        "promotionVersion": "1.0.0",
    }
    for name, expected in expected_scalars.items():
        if policy[name] != expected:
            errors.append(f"{name} must be {expected!r}; got {policy[name]!r}")
    if policy["state"] not in {"pre-freeze", "frozen"}:
        errors.append("state must be 'pre-freeze' or 'frozen'")

    activation = policy["activation"]
    activation_fields = {
        "candidateRevision",
        "activatedUtc",
        "baselineManifest",
        "baselineSha256",
    }
    _strict_keys(activation, activation_fields, "activation", errors)

    prerequisites = policy["prerequisiteGates"]
    prerequisite_ids: list[str] = []
    prerequisite_states: list[str] = []
    if not isinstance(prerequisites, list):
        errors.append("prerequisiteGates must be an array")
    else:
        for index, gate in enumerate(prerequisites):
            context = f"prerequisiteGates[{index}]"
            if not _strict_keys(gate, {"id", "state"}, context, errors):
                continue
            prerequisite_ids.append(gate["id"])
            prerequisite_states.append(gate["state"])
            if gate["state"] not in {"open", "passed"}:
                errors.append(f"{context}.state must be 'open' or 'passed'")
        if tuple(prerequisite_ids) != EXPECTED_PREREQUISITES:
            errors.append(f"prerequisite gate IDs must be {list(EXPECTED_PREREQUISITES)!r}; got {prerequisite_ids!r}")

    if tuple(policy["allowedChangeKinds"]) != EXPECTED_CHANGE_KINDS:
        errors.append(f"allowedChangeKinds must be {list(EXPECTED_CHANGE_KINDS)!r}")
    if tuple(policy["requiredChangeEvidence"]) != EXPECTED_CHANGE_EVIDENCE:
        errors.append(f"requiredChangeEvidence must be {list(EXPECTED_CHANGE_EVIDENCE)!r}")

    severities = policy["severityPolicy"]
    severity_effects: dict[str, str] = {}
    if not isinstance(severities, list):
        errors.append("severityPolicy must be an array")
    else:
        for index, severity in enumerate(severities):
            context = f"severityPolicy[{index}]"
            if not _strict_keys(severity, {"id", "releaseEffect"}, context, errors):
                continue
            severity_effects[severity["id"]] = severity["releaseEffect"]
        if severity_effects != EXPECTED_SEVERITY_EFFECTS:
            errors.append(f"severityPolicy must be {EXPECTED_SEVERITY_EFFECTS!r}")

    surfaces = _resolve_surfaces(root, policy["frozenContracts"], errors)
    if not isinstance(activation, dict):
        return errors, surfaces

    activation_values = [activation.get(field) for field in sorted(activation_fields)]
    if policy["state"] == "pre-freeze":
        if any(value is not None for value in activation_values):
            errors.append("pre-freeze activation fields must all be null")
    else:
        if prerequisite_states != ["passed"] * len(EXPECTED_PREREQUISITES):
            errors.append("every prerequisite gate must pass before the policy is frozen")
        if not REVISION_PATTERN.fullmatch(str(activation.get("candidateRevision"))):
            errors.append("candidateRevision must be a lowercase 40-character Git revision")
        if not UTC_PATTERN.fullmatch(str(activation.get("activatedUtc"))):
            errors.append("activatedUtc must be a second-precision UTC timestamp")
        manifest = activation.get("baselineManifest")
        if not _safe_pattern(manifest) or not str(manifest).startswith("config/"):
            errors.append("baselineManifest must be a safe repository-relative config path")
        if not SHA256_PATTERN.fullmatch(str(activation.get("baselineSha256"))):
            errors.append("baselineSha256 must be a lowercase SHA-256 digest")
    return errors, surfaces


def _file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _combined_digest(files: list[dict[str, Any]]) -> str:
    digest = hashlib.sha256()
    for entry in files:
        line = f"{entry['path']}\0{entry['sha256']}\0{','.join(entry['contractIds'])}\n"
        digest.update(line.encode("utf-8"))
    return digest.hexdigest()


def build_manifest(
    root: Path,
    policy: dict[str, Any],
    surfaces: dict[str, tuple[str, ...]],
    revision: str,
    generated_utc: str,
) -> dict[str, Any]:
    """Build a deterministic baseline manifest for an already reviewed source tree."""
    if not REVISION_PATTERN.fullmatch(revision):
        raise ValueError("revision must be a lowercase 40-character Git revision")
    if not UTC_PATTERN.fullmatch(generated_utc):
        raise ValueError("generated UTC must use YYYY-MM-DDTHH:MM:SSZ")
    files = [
        {
            "path": path,
            "sha256": _file_sha256(root / path),
            "contractIds": list(contract_ids),
        }
        for path, contract_ids in surfaces.items()
    ]
    return {
        "schemaVersion": 1,
        "kind": "candidate-freeze-baseline-v1",
        "policyId": policy["policyId"],
        "candidateVersion": policy["candidateVersion"],
        "candidateRevision": revision,
        "generatedUtc": generated_utc,
        "files": files,
        "combinedSha256": _combined_digest(files),
    }


def _validate_manifest(
    root: Path,
    policy: dict[str, Any],
    surfaces: dict[str, tuple[str, ...]],
    errors: list[str],
) -> None:
    activation = policy["activation"]
    manifest_path = root / activation["baselineManifest"]
    if not manifest_path.is_file():
        errors.append(f"baseline manifest is missing: {activation['baselineManifest']}")
        return
    manifest_hash = _file_sha256(manifest_path)
    if manifest_hash != activation["baselineSha256"]:
        errors.append("baseline manifest SHA-256 does not match the activation record")
        return
    try:
        manifest = _read_json(manifest_path)
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"baseline manifest is unreadable: {exc}")
        return
    expected_fields = {
        "schemaVersion",
        "kind",
        "policyId",
        "candidateVersion",
        "candidateRevision",
        "generatedUtc",
        "files",
        "combinedSha256",
    }
    if not _strict_keys(manifest, expected_fields, "baseline manifest", errors):
        return
    expected_scalars = {
        "schemaVersion": 1,
        "kind": "candidate-freeze-baseline-v1",
        "policyId": policy["policyId"],
        "candidateVersion": policy["candidateVersion"],
        "candidateRevision": activation["candidateRevision"],
        "generatedUtc": activation["activatedUtc"],
    }
    for name, expected in expected_scalars.items():
        if manifest[name] != expected:
            errors.append(f"baseline manifest {name} must be {expected!r}")
    expected_files = [
        {
            "path": path,
            "sha256": _file_sha256(root / path),
            "contractIds": list(contract_ids),
        }
        for path, contract_ids in surfaces.items()
    ]
    if manifest["files"] != expected_files:
        errors.append("frozen contract files differ from the baseline manifest")
    expected_combined = _combined_digest(expected_files)
    if manifest["combinedSha256"] != expected_combined:
        errors.append("baseline combined SHA-256 does not match current frozen contracts")


def validate_policy(root: Path, policy_path: Path) -> tuple[list[str], int]:
    """Return validation errors and the resolved frozen-file count."""
    try:
        policy = _read_json(policy_path)
    except (OSError, json.JSONDecodeError) as exc:
        return [f"candidate freeze policy is unreadable: {exc}"], 0
    errors, surfaces = _validate_policy_shape(root, policy)
    if not errors and policy["state"] == "frozen":
        _validate_manifest(root, policy, surfaces, errors)
    return errors, len(surfaces)


def main(argv: list[str] | None = None) -> int:
    """Validate the policy, or produce a reviewed pre-activation baseline."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepare-baseline", action="store_true")
    parser.add_argument("--revision")
    parser.add_argument("--generated-utc")
    parser.add_argument(
        "--output",
        type=Path,
        default=ROOT / "config" / "candidate_freeze_baseline_v1.json",
    )
    args = parser.parse_args(argv)

    errors, file_count = validate_policy(ROOT, POLICY_PATH)
    if errors:
        print("Candidate freeze policy check failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1

    policy = _read_json(POLICY_PATH)
    if not args.prepare_baseline:
        print(f"Candidate freeze policy check passed for {file_count} frozen-surface files ({policy['state']}).")
        return 0

    if policy["state"] != "pre-freeze":
        print("A baseline can only be prepared while the policy is pre-freeze.", file=sys.stderr)
        return 1
    if any(gate["state"] != "passed" for gate in policy["prerequisiteGates"]):
        print("Every prerequisite gate must pass before preparing a baseline.", file=sys.stderr)
        return 1
    if args.revision is None or args.generated_utc is None:
        print("--revision and --generated-utc are required for baseline preparation.", file=sys.stderr)
        return 1

    shape_errors, surfaces = _validate_policy_shape(ROOT, policy)
    if shape_errors:
        return 1
    try:
        manifest = build_manifest(ROOT, policy, surfaces, args.revision, args.generated_utc)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    args.output.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared candidate freeze baseline with {len(manifest['files'])} files at {args.output}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
