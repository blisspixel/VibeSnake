"""Validate the V090-07 manual product matrix contract and retained sessions."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config" / "qa_manual_product_matrix_v1.json"
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
UTC_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")
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
    ("keyboard", "complete-required-flow"),
    ("mouse", "menu-settings-gameplay-direction-back"),
    ("xbox-layout-controller", "complete-required-flow"),
    ("playstation-layout-controller", "complete-required-flow"),
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
    "inputDeviceIds",
    "settingsProfileIds",
    "executedUtc",
    "results",
)
RESULT_FIELDS = ("flowId", "result", "evidencePaths")
RESULT_VALUES = ("pass", "fail", "blocked")
RELEASE_RULES = (
    "Every required flow must pass on every platform row using the exact candidate artifact.",
    "Keyboard, mouse, Xbox-layout controller, and PlayStation-layout controller coverage must be retained.",
    "Every required settings profile must be observed at least once and on every platform where the behavior is platform-dependent.",
    "A failed or blocked required flow prevents release acceptance.",
    "An inaccessible required flow is a P1 defect and prevents release acceptance.",
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
        "settingsProfiles",
        "requiredSessionFields",
        "requiredResultFields",
        "resultValues",
        "releaseRules",
    }
    if not _strict_keys(contract, expected_fields, "contract", errors):
        return errors, contract if isinstance(contract, dict) else None

    _exact(contract["schemaVersion"], 1, "contract.schemaVersion", errors)
    _exact(contract["kind"], "vibesnake-manual-product-matrix-v1", "contract.kind", errors)
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
        contract["settingsProfiles"],
        list(SETTINGS_PROFILES),
        "contract.settingsProfiles",
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


def _validate_session(
    session: Any,
    path: Path,
    errors: list[str],
) -> tuple[str, str, str, set[str], set[str], dict[str, str]] | None:
    label = f"session {path.name}"
    if not _strict_keys(session, set(SESSION_FIELDS), label, errors):
        return None
    _exact(session["schemaVersion"], 1, f"{label}.schemaVersion", errors)
    _exact(session["kind"], "vibesnake-manual-product-matrix-session-v1", f"{label}.kind", errors)
    session_id = session["sessionId"]
    revision = session["candidateRevision"]
    artifact_sha = session["artifactSha256"]
    platform = session["platformRowId"]
    if not _nonempty_string(session_id, f"{label}.sessionId", errors):
        return None
    if not REVISION_PATTERN.fullmatch(str(revision)):
        errors.append(f"{label}.candidateRevision must be a lowercase 40-character revision")
    if not SHA256_PATTERN.fullmatch(str(artifact_sha)):
        errors.append(f"{label}.artifactSha256 must be a SHA-256 digest")
    if platform not in {row[0] for row in PLATFORM_ROWS}:
        errors.append(f"{label}.platformRowId is unsupported: {platform!r}")
    for field in ("appVersion", "operatingSystemVersion", "hardwareClass", "renderer"):
        _nonempty_string(session[field], f"{label}.{field}", errors)
    if not UTC_PATTERN.fullmatch(str(session["executedUtc"])):
        errors.append(f"{label}.executedUtc must use YYYY-MM-DDTHH:MM:SSZ")

    input_devices = session["inputDeviceIds"]
    if (
        not isinstance(input_devices, list)
        or not input_devices
        or len(input_devices) != len(set(input_devices))
        or not set(input_devices) <= {item[0] for item in INPUT_DEVICES}
    ):
        errors.append(f"{label}.inputDeviceIds must be unique supported devices")
        input_devices = []
    profiles = session["settingsProfileIds"]
    if (
        not isinstance(profiles, list)
        or len(profiles) != len(set(profiles))
        or not set(profiles) <= set(SETTINGS_PROFILES)
    ):
        errors.append(f"{label}.settingsProfileIds must be unique supported profiles")
        profiles = []

    results = session["results"]
    result_map: dict[str, str] = {}
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
            result_map[flow_id] = value

    return (
        str(session_id),
        str(revision),
        str(artifact_sha),
        set(input_devices),
        set(profiles),
        result_map,
    )


def validate_manual_product_matrix(
    contract_path: Path = CONTRACT_PATH,
    sessions_directory: Path | None = None,
) -> tuple[list[str], dict[str, Any]]:
    """Validate the handoff and, when supplied, the retained manual sessions."""
    contract_errors, contract = validate_contract(contract_path)
    errors = list(contract_errors)
    contract_sha = hashlib.sha256(contract_path.read_bytes()).hexdigest() if contract_path.is_file() else None
    session_paths = (
        sorted(sessions_directory.glob("*.json"))
        if sessions_directory is not None and sessions_directory.is_dir()
        else []
    )
    if sessions_directory is not None and not sessions_directory.is_dir():
        errors.append(f"sessions directory does not exist: {sessions_directory}")

    platform_passes = {row[0]: set() for row in PLATFORM_ROWS}
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
        session_id, revision, artifact_sha, devices, profiles, results = validated
        if session_id in session_ids:
            errors.append(f"duplicate manual product matrix sessionId: {session_id}")
        session_ids.add(session_id)
        revisions.add(revision)
        observed_devices.update(devices)
        observed_profiles.update(profiles)
        platform = str(session["platformRowId"])
        if platform in platform_passes:
            artifact_hashes[platform].add(artifact_sha)
            for flow_id, result in results.items():
                if result == "pass":
                    platform_passes[platform].add(flow_id)
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
        apple_silicon_hashes = artifact_hashes["macos-universal-apple-silicon"]
        intel_hashes = artifact_hashes["macos-universal-intel"]
        if len(apple_silicon_hashes) == 1 and len(intel_hashes) == 1 and apple_silicon_hashes != intel_hashes:
            errors.append("macOS Apple Silicon and Intel sessions must use the same Universal artifact SHA-256")
        missing_devices = sorted({item[0] for item in INPUT_DEVICES} - observed_devices)
        if missing_devices:
            errors.append(f"manual matrix is missing input devices: {', '.join(missing_devices)}")
        missing_profiles = sorted(set(SETTINGS_PROFILES) - observed_profiles)
        if missing_profiles:
            errors.append(f"manual matrix is missing settings profiles: {', '.join(missing_profiles)}")
        if len(revisions) != 1:
            errors.append("manual matrix sessions must use one candidate revision")
        if failed_or_blocked:
            errors.append("manual matrix contains failed or blocked required flows")
        manual_execution_complete = not errors

    completed_cells = sum(len(flows) for flows in platform_passes.values())
    evidence = {
        "schemaVersion": 1,
        "kind": "manual-product-matrix-handoff-v1",
        "passed": not errors if sessions_directory is not None else not errors and contract is not None,
        "protocolQualified": not contract_errors,
        "contractSha256": contract_sha,
        "platformRowCount": len(PLATFORM_ROWS),
        "requiredFlowCount": len(REQUIRED_FLOWS),
        "requiredPlatformFlowCellCount": len(PLATFORM_ROWS) * len(REQUIRED_FLOWS),
        "inputDeviceCount": len(INPUT_DEVICES),
        "settingsProfileCount": len(SETTINGS_PROFILES),
        "requiredSessionFieldCount": len(SESSION_FIELDS),
        "requiredResultFieldCount": len(RESULT_FIELDS),
        "manualSessionCount": len(session_paths),
        "completedPlatformFlowCellCount": completed_cells,
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
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)

    errors, evidence = validate_manual_product_matrix(
        args.contract.resolve(),
        args.sessions.resolve() if args.sessions is not None else None,
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
