"""Validate the V090-08 external test contract and retained candidate rounds."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config" / "qa_external_validation_v1.json"
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
PARTICIPANT_PATTERN = re.compile(r"external-[0-9]{3}")
SESSION_ID_PATTERN = re.compile(r"external-session-[0-9]{3}")
FINDING_ID_PATTERN = re.compile(r"EXT-[0-9]{3}")
UTC_PATTERN = re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")
ARTIFACT_PLATFORMS = ("windows-x64", "macos-universal", "linux-x64")
COHORTS = (
    ("clean-install-fresh-keyboard", True, True, True),
    ("clean-install-fresh-controller", True, True, True),
    ("accessibility-focused-fresh", True, True, True),
    ("returning-regression", False, True, False),
)
INPUT_DEVICES = (
    "keyboard",
    "mouse",
    "xbox-layout-controller",
    "playstation-layout-controller",
)
ACCESSIBILITY_PROFILES = (
    "default",
    "sound-muted",
    "zero-shake",
    "reduced-motion",
    "flash-free",
    "high-contrast",
    "maximum-text-scale",
)
COMPREHENSION_CHECKS = (
    "death-explanation",
    "recovery-identification",
    "power-route-decision-explanation",
    "escalation-recognition",
    "another-run-intent",
    "another-run-reason",
)
REPORT_FAMILIES = ("defect", "comprehension", "accessibility", "crash")
SEVERITY_VALUES = ("P0", "P1", "P2", "P3")
FINDING_DECISIONS = ("fix", "ship", "not-reproducible")
RESOLUTION_VALUES = ("open", "closed")
SESSION_FIELDS = (
    "schemaVersion",
    "kind",
    "sessionId",
    "participantId",
    "cohortId",
    "candidateRevision",
    "artifactPlatform",
    "artifactSha256",
    "appVersion",
    "cleanInstall",
    "neverSeenRepository",
    "inputDeviceIds",
    "accessibilityProfileIds",
    "executedUtc",
    "distributionId",
    "consentRecordedSeparately",
    "reportFamilyPaths",
    "comprehensionResults",
    "crashObserved",
    "findingIds",
    "evidencePaths",
)
COMPREHENSION_RESULT_FIELDS = ("checkId", "result")
COMPREHENSION_RESULT_VALUES = ("pass", "fail", "blocked")
CANDIDATE_FIELDS = (
    "revision",
    "sourceTreeClean",
    "artifactSha256ByPlatform",
    "startedUtc",
    "supersedesRevision",
    "triggerFindingIds",
    "affectedGateIds",
    "gateRerunEvidencePaths",
)
FINDING_FIELDS = (
    "findingId",
    "sessionIds",
    "severity",
    "reportFamily",
    "affectedGateIds",
    "decision",
    "resolutionStatus",
    "workaround",
    "resolutionRevision",
    "verificationEvidencePaths",
)
PREREQUISITE_PATHS = (
    "config/qa_human_playtest_protocol.json",
    "config/qa_manual_product_matrix_v1.json",
    "docs/guides/ACCESSIBILITY.md",
    "docs/release/MANUAL_PRODUCT_MATRIX.md",
)
PRIVACY_RULES = (
    "Consent records stay outside the repository and separate from observations.",
    "Session JSON contains pseudonymous participant IDs and no identifying free text.",
    "Names, accounts, contact details, device serials, system paths, raw input, raw timing, and unrelated device data are forbidden.",
    "Retained reports are reviewed, de-identified files referenced by safe relative paths.",
)
RELEASE_RULES = (
    "All sessions use candidates declared in the clean candidate ledger and exact platform artifact hashes.",
    "Every required cohort and artifact platform is represented on the final candidate.",
    "Fresh cohorts use clean installs, have never seen the repository, and pass every comprehension check.",
    "Keyboard, mouse, Xbox-layout controller, and PlayStation-layout controller coverage is retained.",
    "Defect, comprehension, accessibility, and crash outcomes are retained for every session.",
    "Every fix starts a new clean candidate and reruns every declared affected gate.",
    "No P0 or P1 finding remains open, and every P2 is closed by a fix or an explicit ship decision with a player-facing workaround.",
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


def _existing_relative_paths(value: Any, base: Path, label: str, errors: list[str]) -> list[str]:
    if (
        not isinstance(value, list)
        or not value
        or not all(isinstance(item, str) for item in value)
        or len(value) != len(set(value))
        or not all(_safe_relative_path(item) for item in value)
    ):
        errors.append(f"{label} must contain safe relative paths")
        return []
    missing = [item for item in value if not (base / item).is_file()]
    if missing:
        errors.append(f"{label} reference missing retained files: {', '.join(missing)}")
        return []
    return list(value)


def validate_contract(contract_path: Path = CONTRACT_PATH) -> tuple[list[str], dict[str, Any] | None]:
    """Validate the exact external-validation handoff contract."""
    errors: list[str] = []
    contract = _read_json(contract_path, "external validation contract", errors)
    expected_fields = {
        "schemaVersion",
        "kind",
        "status",
        "participantIdPattern",
        "artifactPlatforms",
        "cohorts",
        "inputDevices",
        "accessibilityProfiles",
        "comprehensionChecks",
        "reportFamilies",
        "severityValues",
        "findingDecisions",
        "resolutionValues",
        "requiredSessionFields",
        "requiredComprehensionResultFields",
        "comprehensionResultValues",
        "requiredCandidateFields",
        "requiredFindingFields",
        "prerequisitePaths",
        "privacyRules",
        "releaseRules",
    }
    if not _strict_keys(contract, expected_fields, "contract", errors):
        return errors, contract if isinstance(contract, dict) else None

    _exact(contract["schemaVersion"], 1, "contract.schemaVersion", errors)
    _exact(contract["kind"], "vibesnake-external-validation-v1", "contract.kind", errors)
    _exact(contract["status"], "qualified-handoff-execution-pending", "contract.status", errors)
    _exact(contract["participantIdPattern"], PARTICIPANT_PATTERN.pattern, "contract participant pattern", errors)
    _exact(contract["artifactPlatforms"], list(ARTIFACT_PLATFORMS), "contract artifact platforms", errors)
    _exact(
        contract["cohorts"],
        [
            {
                "id": cohort_id,
                "freshParticipantRequired": fresh,
                "cleanInstallRequired": clean,
                "neverSeenRepositoryRequired": unseen,
            }
            for cohort_id, fresh, clean, unseen in COHORTS
        ],
        "contract cohorts",
        errors,
    )
    exact_lists = (
        ("inputDevices", INPUT_DEVICES),
        ("accessibilityProfiles", ACCESSIBILITY_PROFILES),
        ("comprehensionChecks", COMPREHENSION_CHECKS),
        ("reportFamilies", REPORT_FAMILIES),
        ("severityValues", SEVERITY_VALUES),
        ("findingDecisions", FINDING_DECISIONS),
        ("resolutionValues", RESOLUTION_VALUES),
        ("requiredSessionFields", SESSION_FIELDS),
        ("requiredComprehensionResultFields", COMPREHENSION_RESULT_FIELDS),
        ("comprehensionResultValues", COMPREHENSION_RESULT_VALUES),
        ("requiredCandidateFields", CANDIDATE_FIELDS),
        ("requiredFindingFields", FINDING_FIELDS),
        ("prerequisitePaths", PREREQUISITE_PATHS),
        ("privacyRules", PRIVACY_RULES),
        ("releaseRules", RELEASE_RULES),
    )
    for field, expected in exact_lists:
        _exact(contract[field], list(expected), f"contract.{field}", errors)
    return errors, contract


def _validate_candidate_ledger(path: Path, errors: list[str]) -> list[dict[str, Any]]:
    ledger = _read_json(path, "candidate ledger", errors)
    if not _strict_keys(ledger, {"schemaVersion", "kind", "candidates"}, "candidate ledger", errors):
        return []
    _exact(ledger["schemaVersion"], 1, "candidate ledger.schemaVersion", errors)
    _exact(ledger["kind"], "vibesnake-external-candidate-ledger-v1", "candidate ledger.kind", errors)
    candidates = ledger["candidates"]
    if not isinstance(candidates, list) or not candidates:
        errors.append("candidate ledger.candidates must be a nonempty array")
        return []

    validated: list[dict[str, Any]] = []
    revisions: set[str] = set()
    prior_revision: str | None = None
    prior_started_utc: str | None = None
    for index, candidate in enumerate(candidates):
        record_error_count = len(errors)
        label = f"candidate ledger.candidates[{index}]"
        if not _strict_keys(candidate, set(CANDIDATE_FIELDS), label, errors):
            continue
        revision = str(candidate["revision"])
        if not REVISION_PATTERN.fullmatch(revision):
            errors.append(f"{label}.revision must be a lowercase 40-character revision")
        if revision in revisions:
            errors.append(f"duplicate candidate revision: {revision}")
        revisions.add(revision)
        _exact(candidate["sourceTreeClean"], True, f"{label}.sourceTreeClean", errors)
        if not UTC_PATTERN.fullmatch(str(candidate["startedUtc"])):
            errors.append(f"{label}.startedUtc must use YYYY-MM-DDTHH:MM:SSZ")
        elif prior_started_utc is not None and str(candidate["startedUtc"]) <= prior_started_utc:
            errors.append(f"{label}.startedUtc must be later than the previous candidate")
        hashes = candidate["artifactSha256ByPlatform"]
        if not _strict_keys(hashes, set(ARTIFACT_PLATFORMS), f"{label}.artifactSha256ByPlatform", errors):
            hashes = {}
        elif not all(SHA256_PATTERN.fullmatch(str(value)) for value in hashes.values()):
            errors.append(f"{label}.artifactSha256ByPlatform must contain SHA-256 digests")

        triggers = candidate["triggerFindingIds"]
        affected_gates = candidate["affectedGateIds"]
        rerun_paths = candidate["gateRerunEvidencePaths"]
        if index == 0:
            _exact(candidate["supersedesRevision"], None, f"{label}.supersedesRevision", errors)
            _exact(triggers, [], f"{label}.triggerFindingIds", errors)
            _exact(affected_gates, [], f"{label}.affectedGateIds", errors)
            _exact(rerun_paths, {}, f"{label}.gateRerunEvidencePaths", errors)
        else:
            _exact(candidate["supersedesRevision"], prior_revision, f"{label}.supersedesRevision", errors)
            if (
                not isinstance(triggers, list)
                or not triggers
                or not all(isinstance(item, str) for item in triggers)
                or len(triggers) != len(set(triggers))
                or not all(FINDING_ID_PATTERN.fullmatch(item) for item in triggers)
            ):
                errors.append(f"{label}.triggerFindingIds must contain unique finding IDs")
            if (
                not isinstance(affected_gates, list)
                or not affected_gates
                or not all(isinstance(item, str) for item in affected_gates)
                or len(affected_gates) != len(set(affected_gates))
                or not all(_nonempty_string(item, f"{label}.affectedGateIds", errors) for item in affected_gates)
            ):
                errors.append(f"{label}.affectedGateIds must contain unique gate IDs")
            if (
                isinstance(affected_gates, list)
                and all(isinstance(item, str) for item in affected_gates)
                and _strict_keys(
                    rerun_paths,
                    set(affected_gates),
                    f"{label}.gateRerunEvidencePaths",
                    errors,
                )
            ):
                for gate_id in affected_gates:
                    _existing_relative_paths(
                        rerun_paths[gate_id],
                        path.parent,
                        f"{label}.gateRerunEvidencePaths.{gate_id}",
                        errors,
                    )
        prior_revision = revision
        prior_started_utc = str(candidate["startedUtc"])
        if len(errors) == record_error_count:
            validated.append(candidate)
    return validated


def _validate_findings(path: Path, errors: list[str]) -> list[dict[str, Any]]:
    review = _read_json(path, "finding review", errors)
    if not _strict_keys(review, {"schemaVersion", "kind", "findings"}, "finding review", errors):
        return []
    _exact(review["schemaVersion"], 1, "finding review.schemaVersion", errors)
    _exact(review["kind"], "vibesnake-external-finding-review-v1", "finding review.kind", errors)
    findings = review["findings"]
    if not isinstance(findings, list):
        errors.append("finding review.findings must be an array")
        return []

    validated: list[dict[str, Any]] = []
    finding_ids: set[str] = set()
    for index, finding in enumerate(findings):
        record_error_count = len(errors)
        label = f"finding review.findings[{index}]"
        if not _strict_keys(finding, set(FINDING_FIELDS), label, errors):
            continue
        finding_id = str(finding["findingId"])
        if not FINDING_ID_PATTERN.fullmatch(finding_id):
            errors.append(f"{label}.findingId must match EXT-[0-9]{{3}}")
        if finding_id in finding_ids:
            errors.append(f"duplicate findingId: {finding_id}")
        finding_ids.add(finding_id)
        session_ids = finding["sessionIds"]
        if (
            not isinstance(session_ids, list)
            or not session_ids
            or not all(isinstance(item, str) for item in session_ids)
            or len(session_ids) != len(set(session_ids))
            or not all(SESSION_ID_PATTERN.fullmatch(item) for item in session_ids)
        ):
            errors.append(f"{label}.sessionIds must contain unique external session IDs")
        severity = finding["severity"] if isinstance(finding["severity"], str) else ""
        report_family = finding["reportFamily"] if isinstance(finding["reportFamily"], str) else ""
        if severity not in SEVERITY_VALUES:
            errors.append(f"{label}.severity is unsupported")
        if report_family not in REPORT_FAMILIES:
            errors.append(f"{label}.reportFamily is unsupported")
        affected_gates = finding["affectedGateIds"]
        if (
            not isinstance(affected_gates, list)
            or not affected_gates
            or not all(isinstance(item, str) for item in affected_gates)
            or len(affected_gates) != len(set(affected_gates))
            or not all(_nonempty_string(item, f"{label}.affectedGateIds", errors) for item in affected_gates)
        ):
            errors.append(f"{label}.affectedGateIds must be nonempty")
        decision = finding["decision"] if isinstance(finding["decision"], str) else ""
        resolution = finding["resolutionStatus"] if isinstance(finding["resolutionStatus"], str) else ""
        if decision not in FINDING_DECISIONS:
            errors.append(f"{label}.decision is unsupported")
        if resolution not in RESOLUTION_VALUES:
            errors.append(f"{label}.resolutionStatus is unsupported")
        if severity in {"P0", "P1"} and decision == "ship":
            errors.append(f"{label} cannot ship a P0 or P1 finding")
        if severity == "P2" and decision not in {"fix", "ship"}:
            errors.append(f"{label} P2 decision must be fix or ship")
        if (
            severity == "P2"
            and decision == "ship"
            and not _nonempty_string(finding["workaround"], f"{label}.workaround", errors)
        ):
            errors.append(f"{label} requires a player-facing workaround")
        if resolution == "closed":
            if decision == "fix" and not REVISION_PATTERN.fullmatch(str(finding["resolutionRevision"])):
                errors.append(f"{label}.resolutionRevision must identify the fixed candidate")
            _existing_relative_paths(
                finding["verificationEvidencePaths"],
                path.parent,
                f"{label}.verificationEvidencePaths",
                errors,
            )
        else:
            _exact(finding["resolutionRevision"], None, f"{label}.resolutionRevision", errors)
            _exact(finding["verificationEvidencePaths"], [], f"{label}.verificationEvidencePaths", errors)
        if decision != "fix":
            _exact(finding["resolutionRevision"], None, f"{label}.resolutionRevision", errors)
        if len(errors) == record_error_count:
            validated.append(finding)
    return validated


def _validate_session(path: Path, errors: list[str]) -> dict[str, Any] | None:
    record_error_count = len(errors)
    session = _read_json(path, "external validation session", errors)
    label = f"session {path.name}"
    if not _strict_keys(session, set(SESSION_FIELDS), label, errors):
        return None
    _exact(session["schemaVersion"], 1, f"{label}.schemaVersion", errors)
    _exact(session["kind"], "vibesnake-external-validation-session-v1", f"{label}.kind", errors)
    if not SESSION_ID_PATTERN.fullmatch(str(session["sessionId"])):
        errors.append(f"{label}.sessionId must match external-session-[0-9]{{3}}")
    if not PARTICIPANT_PATTERN.fullmatch(str(session["participantId"])):
        errors.append(f"{label}.participantId must match {PARTICIPANT_PATTERN.pattern}")
    cohort_id = session["cohortId"] if isinstance(session["cohortId"], str) else ""
    cohort_by_id = {item[0]: item for item in COHORTS}
    cohort = cohort_by_id.get(cohort_id)
    if cohort is None:
        errors.append(f"{label}.cohortId is unsupported")
    if not REVISION_PATTERN.fullmatch(str(session["candidateRevision"])):
        errors.append(f"{label}.candidateRevision must be a lowercase 40-character revision")
    if session["artifactPlatform"] not in ARTIFACT_PLATFORMS:
        errors.append(f"{label}.artifactPlatform is unsupported")
    if not SHA256_PATTERN.fullmatch(str(session["artifactSha256"])):
        errors.append(f"{label}.artifactSha256 must be a SHA-256 digest")
    _nonempty_string(session["appVersion"], f"{label}.appVersion", errors)
    if not isinstance(session["cleanInstall"], bool) or not isinstance(session["neverSeenRepository"], bool):
        errors.append(f"{label} cleanInstall and neverSeenRepository must be booleans")
    if cohort is not None:
        _, _, clean_required, unseen_required = cohort
        if clean_required:
            _exact(session["cleanInstall"], True, f"{label}.cleanInstall", errors)
        if unseen_required:
            _exact(session["neverSeenRepository"], True, f"{label}.neverSeenRepository", errors)
    devices = session["inputDeviceIds"]
    if (
        not isinstance(devices, list)
        or not devices
        or not all(isinstance(item, str) for item in devices)
        or len(devices) != len(set(devices))
        or not set(devices) <= set(INPUT_DEVICES)
    ):
        errors.append(f"{label}.inputDeviceIds must contain unique supported devices")
    if cohort_id == "clean-install-fresh-keyboard" and (not isinstance(devices, list) or "keyboard" not in devices):
        errors.append(f"{label} keyboard cohort must use the keyboard")
    if cohort_id == "clean-install-fresh-controller" and (
        not isinstance(devices, list)
        or not all(isinstance(item, str) for item in devices)
        or not set(devices) & {"xbox-layout-controller", "playstation-layout-controller"}
    ):
        errors.append(f"{label} controller cohort must use a supported controller")
    profiles = session["accessibilityProfileIds"]
    if (
        not isinstance(profiles, list)
        or not profiles
        or not all(isinstance(item, str) for item in profiles)
        or len(profiles) != len(set(profiles))
        or not set(profiles) <= set(ACCESSIBILITY_PROFILES)
    ):
        errors.append(f"{label}.accessibilityProfileIds must contain unique supported profiles")
    if cohort_id == "accessibility-focused-fresh" and (
        not isinstance(profiles, list)
        or not all(isinstance(item, str) for item in profiles)
        or set(profiles) <= {"default"}
    ):
        errors.append(f"{label} accessibility cohort must use a non-default profile")
    if not UTC_PATTERN.fullmatch(str(session["executedUtc"])):
        errors.append(f"{label}.executedUtc must use YYYY-MM-DDTHH:MM:SSZ")
    _nonempty_string(session["distributionId"], f"{label}.distributionId", errors)
    _exact(session["consentRecordedSeparately"], True, f"{label}.consentRecordedSeparately", errors)
    report_paths = session["reportFamilyPaths"]
    if _strict_keys(report_paths, set(REPORT_FAMILIES), f"{label}.reportFamilyPaths", errors):
        for family in REPORT_FAMILIES:
            _existing_relative_paths(report_paths[family], path.parent, f"{label}.reportFamilyPaths.{family}", errors)
    results = session["comprehensionResults"]
    result_map: dict[str, str] = {}
    if not isinstance(results, list):
        errors.append(f"{label}.comprehensionResults must be an array")
    else:
        for index, result in enumerate(results):
            result_label = f"{label}.comprehensionResults[{index}]"
            if not _strict_keys(result, set(COMPREHENSION_RESULT_FIELDS), result_label, errors):
                continue
            check_id = result["checkId"]
            if not isinstance(check_id, str) or check_id not in COMPREHENSION_CHECKS or check_id in result_map:
                errors.append(f"{result_label}.checkId must be unique and supported")
                continue
            if result["result"] not in COMPREHENSION_RESULT_VALUES:
                errors.append(f"{result_label}.result is unsupported")
                continue
            result_map[check_id] = result["result"]
    if set(result_map) != set(COMPREHENSION_CHECKS):
        errors.append(f"{label}.comprehensionResults must cover every required check")
    if cohort is not None and cohort[1] and any(value != "pass" for value in result_map.values()):
        errors.append(f"{label} fresh participant must pass every comprehension check")
    if not isinstance(session["crashObserved"], bool):
        errors.append(f"{label}.crashObserved must be a boolean")
    finding_ids = session["findingIds"]
    if (
        not isinstance(finding_ids, list)
        or not all(isinstance(item, str) for item in finding_ids)
        or len(finding_ids) != len(set(finding_ids))
        or not all(FINDING_ID_PATTERN.fullmatch(item) for item in finding_ids)
    ):
        errors.append(f"{label}.findingIds must contain unique finding IDs")
    _existing_relative_paths(session["evidencePaths"], path.parent, f"{label}.evidencePaths", errors)
    return session if len(errors) == record_error_count else None


def validate_external_validation(
    contract_path: Path = CONTRACT_PATH,
    sessions_directory: Path | None = None,
    candidate_ledger_path: Path | None = None,
    findings_path: Path | None = None,
) -> tuple[list[str], dict[str, Any]]:
    """Validate the protocol and optional retained external-validation records."""
    contract_errors, contract = validate_contract(contract_path)
    errors = list(contract_errors)
    prerequisite_hashes: dict[str, str] = {}
    for relative_path in PREREQUISITE_PATHS:
        path = ROOT / relative_path
        if not path.is_file():
            errors.append(f"missing external validation prerequisite: {relative_path}")
        else:
            prerequisite_hashes[relative_path] = hashlib.sha256(path.read_bytes()).hexdigest()

    supplied = (sessions_directory is not None, candidate_ledger_path is not None, findings_path is not None)
    execution_requested = all(supplied)
    if any(supplied) and not execution_requested:
        errors.append("sessions, candidate ledger, and findings must be supplied together")

    session_paths: list[Path] = []
    sessions: list[dict[str, Any]] = []
    candidates: list[dict[str, Any]] = []
    findings: list[dict[str, Any]] = []
    if execution_requested:
        if not sessions_directory.is_dir():
            errors.append(f"sessions directory does not exist: {sessions_directory}")
        else:
            session_paths = sorted(sessions_directory.glob("*.json"))
            if not session_paths:
                errors.append("sessions directory contains no JSON sessions")
            for path in session_paths:
                session = _validate_session(path, errors)
                if session is not None:
                    sessions.append(session)
        candidates = _validate_candidate_ledger(candidate_ledger_path, errors)
        findings = _validate_findings(findings_path, errors)

    final_revision = str(candidates[-1]["revision"]) if candidates else None
    final_sessions = [item for item in sessions if item["candidateRevision"] == final_revision]
    observed_cohorts = {str(item["cohortId"]) for item in final_sessions}
    observed_platforms = {str(item["artifactPlatform"]) for item in final_sessions}
    observed_devices = {str(device) for item in final_sessions for device in item["inputDeviceIds"]}
    observed_profiles = {str(profile) for item in final_sessions for profile in item["accessibilityProfileIds"]}
    crash_count = sum(bool(item["crashObserved"]) for item in sessions)

    if execution_requested and candidates:
        candidate_by_revision = {str(item["revision"]): item for item in candidates}
        candidate_index_by_revision = {str(item["revision"]): index for index, item in enumerate(candidates)}
        session_ids = [str(item["sessionId"]) for item in sessions]
        if len(session_ids) != len(set(session_ids)):
            errors.append("external validation session IDs must be unique")
        finding_by_id = {str(item["findingId"]): item for item in findings}
        session_by_id = {str(item["sessionId"]): item for item in sessions}
        cohort_by_participant: dict[str, str] = {}
        for session in sessions:
            participant = str(session["participantId"])
            cohort = str(session["cohortId"])
            previous_cohort = cohort_by_participant.setdefault(participant, cohort)
            if previous_cohort != cohort:
                errors.append(f"participant {participant} cannot represent multiple cohorts")
        for session in sessions:
            revision = str(session["candidateRevision"])
            candidate = candidate_by_revision.get(revision)
            if candidate is None:
                errors.append(f"session {session['sessionId']} uses an undeclared candidate revision")
                continue
            platform = str(session["artifactPlatform"])
            expected_hash = candidate["artifactSha256ByPlatform"].get(platform)
            if session["artifactSha256"] != expected_hash:
                errors.append(f"session {session['sessionId']} artifact hash does not match its candidate ledger")
            for finding_id in session["findingIds"]:
                if finding_id not in finding_by_id:
                    errors.append(f"session {session['sessionId']} references unknown finding {finding_id}")
        for finding in findings:
            for session_id in finding["sessionIds"]:
                if session_id not in set(session_ids):
                    errors.append(f"finding {finding['findingId']} references unknown session {session_id}")
            if finding["resolutionStatus"] == "closed" and finding["decision"] == "fix":
                resolution_revision = str(finding["resolutionRevision"])
                if resolution_revision not in candidate_by_revision:
                    errors.append(f"finding {finding['findingId']} resolution revision is not in the candidate ledger")
                resolution_candidate = candidate_by_revision.get(resolution_revision)
                if (
                    resolution_candidate is not None
                    and finding["findingId"] not in resolution_candidate["triggerFindingIds"]
                ):
                    errors.append(f"fixed finding {finding['findingId']} is not linked to its replacement candidate")
                resolution_index = candidate_index_by_revision.get(resolution_revision)
                for session_id in finding["sessionIds"]:
                    observed_session = session_by_id.get(str(session_id))
                    if observed_session is None or resolution_index is None:
                        continue
                    observed_index = candidate_index_by_revision.get(str(observed_session["candidateRevision"]))
                    if observed_index is not None and observed_index >= resolution_index:
                        errors.append(
                            f"finding {finding['findingId']} must be observed before its resolution candidate"
                        )
        for candidate in candidates[1:]:
            triggered_findings = [
                finding_by_id[finding_id]
                for finding_id in candidate["triggerFindingIds"]
                if finding_id in finding_by_id
            ]
            for finding_id in candidate["triggerFindingIds"]:
                if finding_id not in finding_by_id:
                    errors.append(f"candidate {candidate['revision']} references unknown trigger finding {finding_id}")
                elif finding_by_id[finding_id]["decision"] != "fix":
                    errors.append(f"candidate {candidate['revision']} trigger {finding_id} is not a fix decision")
            expected_gates = {str(gate_id) for finding in triggered_findings for gate_id in finding["affectedGateIds"]}
            if set(candidate["affectedGateIds"]) != expected_gates:
                errors.append(f"candidate {candidate['revision']} affected gates do not match its trigger findings")
        missing_cohorts = sorted({item[0] for item in COHORTS} - observed_cohorts)
        if missing_cohorts:
            errors.append(f"final candidate is missing cohorts: {', '.join(missing_cohorts)}")
        missing_platforms = sorted(set(ARTIFACT_PLATFORMS) - observed_platforms)
        if missing_platforms:
            errors.append(f"final candidate is missing artifact platforms: {', '.join(missing_platforms)}")
        missing_devices = sorted(set(INPUT_DEVICES) - observed_devices)
        if missing_devices:
            errors.append(f"final candidate is missing input devices: {', '.join(missing_devices)}")
        fresh_by_cohort = {cohort[0]: cohort[1] for cohort in COHORTS}
        fresh_participants = [
            str(item["participantId"]) for item in final_sessions if fresh_by_cohort[item["cohortId"]]
        ]
        if len(fresh_participants) != len(set(fresh_participants)):
            errors.append("fresh final-candidate sessions must use distinct participants")
        for revision in candidate_by_revision:
            app_versions = {str(item["appVersion"]) for item in sessions if item["candidateRevision"] == revision}
            if len(app_versions) > 1:
                errors.append(f"candidate {revision} sessions must use one application version")
        unresolved_blockers = [
            str(item["findingId"])
            for item in findings
            if item["severity"] in {"P0", "P1", "P2"} and item["resolutionStatus"] != "closed"
        ]
        if unresolved_blockers:
            errors.append(f"external validation has unresolved blocking findings: {', '.join(unresolved_blockers)}")

    external_validation_complete = execution_requested and bool(final_sessions) and not errors
    evidence = {
        "schemaVersion": 1,
        "kind": "external-validation-handoff-v1",
        "passed": not errors if execution_requested else not errors and contract is not None,
        "protocolQualified": not contract_errors and len(prerequisite_hashes) == len(PREREQUISITE_PATHS),
        "contractSha256": hashlib.sha256(contract_path.read_bytes()).hexdigest() if contract_path.is_file() else None,
        "prerequisiteSha256": prerequisite_hashes,
        "artifactPlatformCount": len(ARTIFACT_PLATFORMS),
        "cohortCount": len(COHORTS),
        "comprehensionCheckCount": len(COMPREHENSION_CHECKS),
        "reportFamilyCount": len(REPORT_FAMILIES),
        "inputDeviceCount": len(INPUT_DEVICES),
        "accessibilityProfileCount": len(ACCESSIBILITY_PROFILES),
        "requiredSessionFieldCount": len(SESSION_FIELDS),
        "requiredCandidateFieldCount": len(CANDIDATE_FIELDS),
        "requiredFindingFieldCount": len(FINDING_FIELDS),
        "candidateCount": len(candidates),
        "sessionCount": len(sessions),
        "finalCandidateSessionCount": len(final_sessions),
        "findingCount": len(findings),
        "crashObservedCount": crash_count,
        "observedFinalCandidateCohorts": sorted(observed_cohorts),
        "observedFinalCandidatePlatforms": sorted(observed_platforms),
        "observedFinalCandidateInputDevices": sorted(observed_devices),
        "observedFinalCandidateAccessibilityProfiles": sorted(observed_profiles),
        "externalValidationComplete": external_validation_complete,
        "releaseAcceptance": external_validation_complete,
        "pendingGates": []
        if external_validation_complete
        else [
            "controlled-real-artifact-distribution",
            "clean-install-fresh-participants",
            "structured-defect-comprehension-accessibility-crash-reports",
            "fresh-participant-comprehension-and-replay-intent",
            "clean-candidate-fix-and-gate-rerun-loop",
        ],
        "errors": errors,
    }
    return errors, evidence


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--contract", type=Path, default=CONTRACT_PATH)
    parser.add_argument("--sessions", type=Path)
    parser.add_argument("--candidate-ledger", type=Path)
    parser.add_argument("--findings", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)

    errors, evidence = validate_external_validation(
        args.contract.resolve(),
        args.sessions.resolve() if args.sessions is not None else None,
        args.candidate_ledger.resolve() if args.candidate_ledger is not None else None,
        args.findings.resolve() if args.findings is not None else None,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if errors:
        print("External validation qualification failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    if args.sessions is None:
        print("External validation handoff qualified; controlled participant execution remains pending.")
    else:
        print(f"External validation accepted {evidence['sessionCount']} retained sessions.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
