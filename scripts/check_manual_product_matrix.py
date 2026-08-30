"""Validate the V090-07 manual product matrix contract and retained sessions."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from datetime import datetime
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config" / "qa_manual_product_matrix_v2.json"
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
UTC_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")
SESSION_ID_PATTERN = re.compile(r"product-matrix-[0-9]{3}")
RELEASE_RUN_URL_PATTERN = re.compile(r"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/actions/runs/([1-9][0-9]*)")
MAXIMUM_JSON_BYTES = 4 * 1024 * 1024
PLATFORM_ROWS = (
    ("windows-x64", "windows-x64", "x86_64"),
    ("macos-universal-apple-silicon", "macos-universal", "arm64"),
    ("macos-universal-intel", "macos-universal", "x86_64"),
    ("linux-x64", "linux-x64", "x86_64"),
)
REQUIRED_FLOWS = (
    "first-launch",
    "tutorial",
    "classic-mode",
    "vibe-mode",
    "death-self-collision",
    "death-starvation",
    "power-shield",
    "power-phase-shift",
    "power-last-stand",
    "power-slow-mo",
    "power-boost",
    "power-magnet",
    "power-bait",
    "power-gluttony",
    "power-segment-detach",
    "settings-gameplay",
    "settings-controls",
    "settings-audio",
    "settings-display",
    "settings-accessibility",
    "settings-data",
    "achievements",
    "customization",
    "scores",
    "radio",
    "optional-pack-absent",
    "optional-pack-valid",
    "optional-pack-removed",
    "optional-pack-invalid",
    "optional-pack-recovered",
    "ai-channels",
    "replays",
    "reset",
    "recovery",
    "focus-loss",
    "quit",
)
INPUT_DEVICES = (
    ("keyboard", "complete-required-flow-per-platform"),
    ("mouse", "complete-capability-set-per-platform"),
    ("xbox-layout-controller", "complete-required-flow-per-platform"),
    ("playstation-layout-controller", "complete-required-flow-per-platform"),
)
COMPLETE_FLOW_INPUT_DEVICES = tuple(
    device_id for device_id, coverage in INPUT_DEVICES if coverage == "complete-required-flow-per-platform"
)
MOUSE_INPUT_CAPABILITIES = (
    "menu-targeting",
    "settings-navigation",
    "gameplay-direction",
    "back",
)
SETTINGS_PROFILES = (
    "sound-device-absent",
    "sound-muted",
    "zero-shake",
    "reduced-motion",
    "flash-free",
    "high-contrast",
    "maximum-text-scale",
    "missing-optional-content",
)
SESSION_FIELDS = (
    "schemaVersion",
    "kind",
    "sessionId",
    "candidateRevision",
    "artifactSha256",
    "appVersion",
    "platformRowId",
    "operatingSystemVersion",
    "hardwareClass",
    "renderer",
    "executedUtc",
    "results",
)
RESULT_FIELDS = (
    "flowId",
    "inputDeviceId",
    "inputCapabilityIds",
    "settingsProfileIds",
    "result",
    "evidencePaths",
)
RESULT_VALUES = ("pass", "fail", "blocked")
CANDIDATE_FIELDS = (
    "schemaVersion",
    "kind",
    "releaseRunId",
    "releaseRunUrl",
    "releaseMatrixSha256",
    "candidateRevision",
    "appVersion",
    "buildMode",
    "artifactRows",
    "humanReviewStatus",
    "releaseAcceptance",
    "publicationEligible",
)
CANDIDATE_ARTIFACT_FIELDS = (
    "platformRowId",
    "artifactPlatform",
    "architecture",
    "fileName",
    "sha256",
    "bytes",
    "artifactManifestSha256",
)
RELEASE_RULES = (
    "Every retained session must match an exact candidate record projected from independently verified Release matrix evidence.",
    "Every required flow must pass on every platform row using the exact candidate artifact.",
    "Keyboard, Xbox-layout controller, and PlayStation-layout controller must each pass every required flow on every platform row.",
    "Mouse menu targeting, settings navigation, gameplay direction, and Back must each pass on every platform row.",
    "Every required settings profile must appear on at least one passing observation on every platform row.",
    "Only a passing result earns flow, device, capability, or settings-profile coverage.",
    "A failed or blocked required flow prevents release acceptance.",
    "An inaccessible required flow is a P1 defect and prevents release acceptance.",
)


def _unique_json_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError(f"duplicate JSON field: {key}")
        value[key] = item
    return value


def _reject_json_constant(value: str) -> None:
    raise ValueError(f"non-finite JSON number: {value}")


def _read_json(path: Path, label: str, errors: list[str]) -> Any | None:
    if not path.is_file() or path.is_symlink():
        errors.append(f"missing {label}: {path}")
        return None
    try:
        if path.stat().st_size > MAXIMUM_JSON_BYTES:
            errors.append(f"{label} exceeds the {MAXIMUM_JSON_BYTES}-byte limit: {path}")
            return None
        return json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_unique_json_object,
            parse_constant=_reject_json_constant,
        )
    except (OSError, UnicodeError, ValueError) as exc:
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


def _valid_utc(value: Any) -> bool:
    if not isinstance(value, str) or UTC_PATTERN.fullmatch(value) is None:
        return False
    try:
        datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ")
    except ValueError:
        return False
    return True


def _safe_relative_path(value: Any) -> bool:
    if not isinstance(value, str) or not value or "\\" in value:
        return False
    path = Path(value)
    return not path.is_absolute() and ".." not in path.parts


def validate_contract(contract_path: Path = CONTRACT_PATH) -> tuple[list[str], dict[str, Any] | None]:
    """Validate the exact manual matrix contract."""
    errors: list[str] = []
    contract = _read_json(contract_path, "manual product matrix contract", errors)
    expected_fields = {
        "schemaVersion",
        "kind",
        "status",
        "requiredFlowDefectSeverity",
        "platformRows",
        "requiredFlows",
        "inputDevices",
        "mouseInputCapabilities",
        "settingsProfiles",
        "requiredCandidateFields",
        "requiredCandidateArtifactFields",
        "requiredSessionFields",
        "requiredResultFields",
        "resultValues",
        "releaseRules",
    }
    if not _strict_keys(contract, expected_fields, "contract", errors):
        return errors, contract if isinstance(contract, dict) else None

    _exact(contract["schemaVersion"], 2, "contract.schemaVersion", errors)
    _exact(contract["kind"], "vibesnake-manual-product-matrix-v2", "contract.kind", errors)
    _exact(
        contract["status"],
        "qualified-handoff-execution-pending",
        "contract.status",
        errors,
    )
    _exact(contract["requiredFlowDefectSeverity"], "P1", "contract severity", errors)
    _exact(
        contract["platformRows"],
        [
            {"id": row_id, "artifactPlatform": artifact, "architecture": architecture}
            for row_id, artifact, architecture in PLATFORM_ROWS
        ],
        "contract.platformRows",
        errors,
    )
    _exact(contract["requiredFlows"], list(REQUIRED_FLOWS), "contract.requiredFlows", errors)
    _exact(
        contract["inputDevices"],
        [{"id": device_id, "requiredCoverage": coverage} for device_id, coverage in INPUT_DEVICES],
        "contract.inputDevices",
        errors,
    )
    _exact(
        contract["mouseInputCapabilities"],
        list(MOUSE_INPUT_CAPABILITIES),
        "contract.mouseInputCapabilities",
        errors,
    )
    _exact(
        contract["settingsProfiles"],
        list(SETTINGS_PROFILES),
        "contract.settingsProfiles",
        errors,
    )
    _exact(
        contract["requiredCandidateFields"],
        list(CANDIDATE_FIELDS),
        "contract.requiredCandidateFields",
        errors,
    )
    _exact(
        contract["requiredCandidateArtifactFields"],
        list(CANDIDATE_ARTIFACT_FIELDS),
        "contract.requiredCandidateArtifactFields",
        errors,
    )
    _exact(
        contract["requiredSessionFields"],
        list(SESSION_FIELDS),
        "contract.requiredSessionFields",
        errors,
    )
    _exact(
        contract["requiredResultFields"],
        list(RESULT_FIELDS),
        "contract.requiredResultFields",
        errors,
    )
    _exact(contract["resultValues"], list(RESULT_VALUES), "contract.resultValues", errors)
    _exact(contract["releaseRules"], list(RELEASE_RULES), "contract.releaseRules", errors)
    return errors, contract


def validate_candidate(candidate_path: Path) -> tuple[list[str], dict[str, Any] | None]:
    """Validate the exact Release candidate identity used by retained manual sessions."""
    errors: list[str] = []
    candidate = _read_json(candidate_path, "manual product matrix candidate", errors)
    if not _strict_keys(candidate, set(CANDIDATE_FIELDS), "candidate", errors):
        return errors, candidate if isinstance(candidate, dict) else None
    _exact(candidate["schemaVersion"], 1, "candidate.schemaVersion", errors)
    _exact(candidate["kind"], "vibesnake-manual-product-matrix-candidate-v1", "candidate.kind", errors)
    run_id = candidate["releaseRunId"]
    if type(run_id) is not int or run_id <= 0:
        errors.append("candidate.releaseRunId must be a positive integer")
    run_url_match = RELEASE_RUN_URL_PATTERN.fullmatch(str(candidate["releaseRunUrl"]))
    if run_url_match is None or type(run_id) is not int or run_url_match.group(1) != str(run_id):
        errors.append("candidate.releaseRunUrl must be a GitHub Actions URL for releaseRunId")
    if not SHA256_PATTERN.fullmatch(str(candidate["releaseMatrixSha256"])):
        errors.append("candidate.releaseMatrixSha256 must be a SHA-256 digest")
    if not REVISION_PATTERN.fullmatch(str(candidate["candidateRevision"])):
        errors.append("candidate.candidateRevision must be a lowercase 40-character revision")
    _nonempty_string(candidate["appVersion"], "candidate.appVersion", errors)
    _exact(candidate["buildMode"], "Release", "candidate.buildMode", errors)
    _exact(candidate["humanReviewStatus"], "pending", "candidate.humanReviewStatus", errors)
    _exact(candidate["releaseAcceptance"], False, "candidate.releaseAcceptance", errors)
    _exact(candidate["publicationEligible"], False, "candidate.publicationEligible", errors)

    rows = candidate["artifactRows"]
    if not isinstance(rows, list) or len(rows) != len(PLATFORM_ROWS):
        errors.append(f"candidate.artifactRows must contain exactly {len(PLATFORM_ROWS)} rows")
    else:
        for index, expected in enumerate(PLATFORM_ROWS):
            row = rows[index]
            label = f"candidate.artifactRows[{index}]"
            if not _strict_keys(row, set(CANDIDATE_ARTIFACT_FIELDS), label, errors):
                continue
            platform_row_id, artifact_platform, architecture = expected
            _exact(row["platformRowId"], platform_row_id, f"{label}.platformRowId", errors)
            _exact(row["artifactPlatform"], artifact_platform, f"{label}.artifactPlatform", errors)
            _exact(row["architecture"], architecture, f"{label}.architecture", errors)
            file_name = row["fileName"]
            if (
                not isinstance(file_name, str)
                or not file_name
                or "/" in file_name
                or "\\" in file_name
                or file_name in {".", ".."}
            ):
                errors.append(f"{label}.fileName must be a safe file name")
            if not SHA256_PATTERN.fullmatch(str(row["sha256"])):
                errors.append(f"{label}.sha256 must be a SHA-256 digest")
            if not SHA256_PATTERN.fullmatch(str(row["artifactManifestSha256"])):
                errors.append(f"{label}.artifactManifestSha256 must be a SHA-256 digest")
            if type(row["bytes"]) is not int or row["bytes"] <= 0:
                errors.append(f"{label}.bytes must be a positive integer")
        apple_silicon = rows[1]
        intel = rows[2]
        if isinstance(apple_silicon, dict) and isinstance(intel, dict):
            universal_identity_fields = (
                "artifactPlatform",
                "fileName",
                "sha256",
                "bytes",
                "artifactManifestSha256",
            )
            if any(apple_silicon.get(field) != intel.get(field) for field in universal_identity_fields):
                errors.append("candidate macOS architecture rows must identify one identical Universal artifact")
    return errors, candidate


def _validate_session(
    session: Any,
    path: Path,
    errors: list[str],
) -> tuple[str, str, str, dict[str, tuple[str, str, set[str], set[str]]]] | None:
    label = f"session {path.name}"
    if not _strict_keys(session, set(SESSION_FIELDS), label, errors):
        return None
    _exact(session["schemaVersion"], 2, f"{label}.schemaVersion", errors)
    _exact(session["kind"], "vibesnake-manual-product-matrix-session-v2", f"{label}.kind", errors)
    session_id = session["sessionId"]
    revision = session["candidateRevision"]
    artifact_sha = session["artifactSha256"]
    platform = session["platformRowId"]
    if not SESSION_ID_PATTERN.fullmatch(str(session_id)):
        errors.append(f"{label}.sessionId must match product-matrix-[0-9]{{3}}")
    if not REVISION_PATTERN.fullmatch(str(revision)):
        errors.append(f"{label}.candidateRevision must be a lowercase 40-character revision")
    if not SHA256_PATTERN.fullmatch(str(artifact_sha)):
        errors.append(f"{label}.artifactSha256 must be a SHA-256 digest")
    if not isinstance(platform, str) or platform not in {row[0] for row in PLATFORM_ROWS}:
        errors.append(f"{label}.platformRowId is unsupported: {platform!r}")
    for field in ("appVersion", "operatingSystemVersion", "hardwareClass", "renderer"):
        _nonempty_string(session[field], f"{label}.{field}", errors)
    if not _valid_utc(session["executedUtc"]):
        errors.append(f"{label}.executedUtc must use YYYY-MM-DDTHH:MM:SSZ")

    results = session["results"]
    result_map: dict[str, tuple[str, str, set[str], set[str]]] = {}
    if not isinstance(results, list) or not results:
        errors.append(f"{label}.results must be a nonempty array")
    else:
        for index, result in enumerate(results):
            result_label = f"{label}.results[{index}]"
            if not _strict_keys(result, set(RESULT_FIELDS), result_label, errors):
                continue
            flow_id = result["flowId"]
            value = result["result"]
            if flow_id not in REQUIRED_FLOWS:
                errors.append(f"{result_label}.flowId is unsupported: {flow_id!r}")
                continue
            if flow_id in result_map:
                errors.append(f"{label} contains duplicate flow result: {flow_id}")
                continue
            if value not in RESULT_VALUES:
                errors.append(f"{result_label}.result is unsupported: {value!r}")
                continue
            input_device = result["inputDeviceId"]
            if not isinstance(input_device, str) or input_device not in {item[0] for item in INPUT_DEVICES}:
                errors.append(f"{result_label}.inputDeviceId is unsupported: {input_device!r}")
                continue
            capabilities = result["inputCapabilityIds"]
            if (
                not isinstance(capabilities, list)
                or not all(isinstance(item, str) for item in capabilities)
                or len(capabilities) != len(set(capabilities))
                or not set(capabilities) <= set(MOUSE_INPUT_CAPABILITIES)
            ):
                errors.append(f"{result_label}.inputCapabilityIds must be unique supported capabilities")
                continue
            if input_device != "mouse" and capabilities:
                errors.append(f"{result_label}.inputCapabilityIds must be empty for {input_device}")
                continue
            profiles = result["settingsProfileIds"]
            if (
                not isinstance(profiles, list)
                or not all(isinstance(item, str) for item in profiles)
                or len(profiles) != len(set(profiles))
                or not set(profiles) <= set(SETTINGS_PROFILES)
            ):
                errors.append(f"{result_label}.settingsProfileIds must be unique supported profiles")
                continue
            evidence_paths = result["evidencePaths"]
            if (
                not isinstance(evidence_paths, list)
                or not evidence_paths
                or not all(_safe_relative_path(item) for item in evidence_paths)
            ):
                errors.append(f"{result_label}.evidencePaths must contain safe relative paths")
                continue
            missing_evidence = [item for item in evidence_paths if not (path.parent / item).is_file()]
            if missing_evidence:
                errors.append(
                    f"{result_label}.evidencePaths reference missing retained files: " + ", ".join(missing_evidence)
                )
                continue
            result_map[flow_id] = (
                value,
                input_device,
                set(capabilities),
                set(profiles),
            )

    return (
        str(session_id),
        str(revision),
        str(artifact_sha),
        result_map,
    )


def validate_manual_product_matrix(
    contract_path: Path = CONTRACT_PATH,
    sessions_directory: Path | None = None,
    candidate_path: Path | None = None,
) -> tuple[list[str], dict[str, Any]]:
    """Validate the handoff and, when supplied, the retained manual sessions."""
    contract_errors, contract = validate_contract(contract_path)
    errors = list(contract_errors)
    contract_sha = hashlib.sha256(contract_path.read_bytes()).hexdigest() if contract_path.is_file() else None
    candidate_errors: list[str] = []
    candidate: dict[str, Any] | None = None
    candidate_sha: str | None = None
    if candidate_path is not None:
        candidate_errors, candidate = validate_candidate(candidate_path)
        errors.extend(candidate_errors)
        if candidate_path.is_file():
            candidate_sha = hashlib.sha256(candidate_path.read_bytes()).hexdigest()
    if sessions_directory is not None and candidate_path is None:
        errors.append("retained manual sessions require an exact candidate record")
    candidate_rows = (
        {str(row["platformRowId"]): row for row in candidate["artifactRows"]}
        if candidate is not None
        and isinstance(candidate.get("artifactRows"), list)
        and all(isinstance(row, dict) and "platformRowId" in row for row in candidate["artifactRows"])
        else {}
    )
    session_paths = (
        sorted(sessions_directory.glob("*.json"))
        if sessions_directory is not None and sessions_directory.is_dir()
        else []
    )
    if sessions_directory is not None and not sessions_directory.is_dir():
        errors.append(f"sessions directory does not exist: {sessions_directory}")

    platform_passes = {row[0]: set() for row in PLATFORM_ROWS}
    device_flow_passes = {
        (row[0], device_id): set() for row in PLATFORM_ROWS for device_id in COMPLETE_FLOW_INPUT_DEVICES
    }
    mouse_capability_passes = {row[0]: set() for row in PLATFORM_ROWS}
    platform_profile_passes = {row[0]: set() for row in PLATFORM_ROWS}
    observed_devices: set[str] = set()
    observed_profiles: set[str] = set()
    session_ids: set[str] = set()
    revisions: set[str] = set()
    artifact_hashes = {row[0]: set() for row in PLATFORM_ROWS}
    failed_or_blocked = 0
    for path in session_paths:
        session = _read_json(path, "manual product matrix session", errors)
        validated = _validate_session(session, path, errors)
        if validated is None or not isinstance(session, dict):
            continue
        session_id, revision, artifact_sha, results = validated
        if session_id in session_ids:
            errors.append(f"duplicate manual product matrix sessionId: {session_id}")
        session_ids.add(session_id)
        revisions.add(revision)
        platform = str(session["platformRowId"])
        if platform in platform_passes:
            artifact_hashes[platform].add(artifact_sha)
            candidate_row = candidate_rows.get(platform)
            if candidate is not None and candidate_row is not None:
                if revision != candidate.get("candidateRevision"):
                    errors.append(f"session {session_id} revision does not match the exact candidate")
                if artifact_sha != candidate_row.get("sha256"):
                    errors.append(f"session {session_id} artifact SHA-256 does not match the exact candidate")
                if session.get("appVersion") != candidate.get("appVersion"):
                    errors.append(f"session {session_id} application version does not match the exact candidate")
            for flow_id, (result, input_device, capabilities, profiles) in results.items():
                observed_devices.add(input_device)
                observed_profiles.update(profiles)
                if result == "pass":
                    platform_passes[platform].add(flow_id)
                    platform_profile_passes[platform].update(profiles)
                    if input_device in COMPLETE_FLOW_INPUT_DEVICES:
                        device_flow_passes[(platform, input_device)].add(flow_id)
                    elif input_device == "mouse":
                        mouse_capability_passes[platform].update(capabilities)
                else:
                    failed_or_blocked += 1

    manual_execution_complete = False
    if sessions_directory is not None:
        for platform, passed_flows in platform_passes.items():
            missing = sorted(set(REQUIRED_FLOWS) - passed_flows)
            if missing:
                errors.append(f"{platform} is missing passing flows: {', '.join(missing)}")
            if len(artifact_hashes[platform]) != 1:
                errors.append(f"{platform} must use exactly one candidate artifact SHA-256")
            for input_device in COMPLETE_FLOW_INPUT_DEVICES:
                missing_device_flows = sorted(set(REQUIRED_FLOWS) - device_flow_passes[(platform, input_device)])
                if missing_device_flows:
                    errors.append(
                        f"{platform} {input_device} is missing passing flows: " + ", ".join(missing_device_flows)
                    )
            missing_mouse_capabilities = sorted(set(MOUSE_INPUT_CAPABILITIES) - mouse_capability_passes[platform])
            if missing_mouse_capabilities:
                errors.append(
                    f"{platform} mouse is missing passing capabilities: " + ", ".join(missing_mouse_capabilities)
                )
            missing_platform_profiles = sorted(set(SETTINGS_PROFILES) - platform_profile_passes[platform])
            if missing_platform_profiles:
                errors.append(
                    f"{platform} is missing passing settings profiles: " + ", ".join(missing_platform_profiles)
                )
        apple_silicon_hashes = artifact_hashes["macos-universal-apple-silicon"]
        intel_hashes = artifact_hashes["macos-universal-intel"]
        if len(apple_silicon_hashes) == 1 and len(intel_hashes) == 1 and apple_silicon_hashes != intel_hashes:
            errors.append("macOS Apple Silicon and Intel sessions must use the same Universal artifact SHA-256")
        missing_devices = sorted({item[0] for item in INPUT_DEVICES} - observed_devices)
        if missing_devices:
            errors.append(f"manual matrix is missing input devices: {', '.join(missing_devices)}")
        if len(revisions) != 1:
            errors.append("manual matrix sessions must use one candidate revision")
        if failed_or_blocked:
            errors.append("manual matrix contains failed or blocked required flows")
        manual_execution_complete = not errors

    completed_cells = sum(len(flows) for flows in platform_passes.values())
    completed_device_flow_cells = sum(len(flows) for flows in device_flow_passes.values())
    completed_mouse_capability_cells = sum(len(items) for items in mouse_capability_passes.values())
    completed_platform_profile_cells = sum(len(items) for items in platform_profile_passes.values())
    evidence = {
        "schemaVersion": 2,
        "kind": "manual-product-matrix-handoff-v2",
        "passed": not errors if sessions_directory is not None else not errors and contract is not None,
        "protocolQualified": not contract_errors,
        "contractSha256": contract_sha,
        "candidateQualified": candidate_path is not None and not candidate_errors and candidate is not None,
        "candidateSha256": candidate_sha,
        "candidateRevision": candidate.get("candidateRevision") if candidate is not None else None,
        "candidateReleaseRunId": candidate.get("releaseRunId") if candidate is not None else None,
        "platformRowCount": len(PLATFORM_ROWS),
        "requiredFlowCount": len(REQUIRED_FLOWS),
        "requiredPlatformFlowCellCount": len(PLATFORM_ROWS) * len(REQUIRED_FLOWS),
        "requiredDeviceFlowCellCount": (len(PLATFORM_ROWS) * len(COMPLETE_FLOW_INPUT_DEVICES) * len(REQUIRED_FLOWS)),
        "requiredMouseCapabilityCellCount": len(PLATFORM_ROWS) * len(MOUSE_INPUT_CAPABILITIES),
        "requiredPlatformProfileCellCount": len(PLATFORM_ROWS) * len(SETTINGS_PROFILES),
        "inputDeviceCount": len(INPUT_DEVICES),
        "settingsProfileCount": len(SETTINGS_PROFILES),
        "requiredSessionFieldCount": len(SESSION_FIELDS),
        "requiredResultFieldCount": len(RESULT_FIELDS),
        "requiredCandidateFieldCount": len(CANDIDATE_FIELDS),
        "requiredCandidateArtifactFieldCount": len(CANDIDATE_ARTIFACT_FIELDS),
        "manualSessionCount": len(session_paths),
        "completedPlatformFlowCellCount": completed_cells,
        "completedDeviceFlowCellCount": completed_device_flow_cells,
        "completedMouseCapabilityCellCount": completed_mouse_capability_cells,
        "completedPlatformProfileCellCount": completed_platform_profile_cells,
        "observedInputDevices": sorted(observed_devices),
        "observedSettingsProfiles": sorted(observed_profiles),
        "failedOrBlockedResultCount": failed_or_blocked,
        "manualExecutionComplete": manual_execution_complete,
        "releaseAcceptance": manual_execution_complete and not errors,
        "pendingGates": []
        if manual_execution_complete
        else [
            "retained-windows-x64-full-flow",
            "retained-macos-universal-apple-silicon-full-flow",
            "retained-macos-universal-intel-full-flow",
            "retained-linux-x64-full-flow",
            "physical-input-audio-accessibility-profile-coverage",
        ],
        "errors": errors,
    }
    return errors, evidence


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--contract", type=Path, default=CONTRACT_PATH)
    parser.add_argument("--sessions", type=Path)
    parser.add_argument("--candidate", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)

    errors, evidence = validate_manual_product_matrix(
        args.contract.resolve(),
        args.sessions.resolve() if args.sessions is not None else None,
        args.candidate.resolve() if args.candidate is not None else None,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if errors:
        print("Manual product matrix qualification failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    if args.sessions is None:
        print("Manual product matrix handoff qualified; retained physical execution remains pending.")
    else:
        print(f"Manual product matrix accepted {evidence['manualSessionCount']} retained sessions.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
