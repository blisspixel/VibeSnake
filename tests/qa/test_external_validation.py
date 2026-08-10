"""Contracts for the V090-08 external validation handoff."""

from __future__ import annotations

import copy
import json
from pathlib import Path

from scripts.check_external_validation import (
    ARTIFACT_PLATFORMS,
    COHORTS,
    COMPREHENSION_CHECKS,
    CONTRACT_PATH,
    REPORT_FAMILIES,
    validate_contract,
    validate_external_validation,
)


ARTIFACT_HASHES = {
    "windows-x64": "1" * 64,
    "macos-universal": "2" * 64,
    "linux-x64": "3" * 64,
}


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def _write_evidence(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"retained de-identified external validation evidence")


def _candidate(revision: str, *, prior: str | None = None, trigger: str | None = None) -> dict[str, object]:
    replacement = prior is not None
    return {
        "revision": revision,
        "sourceTreeClean": True,
        "artifactSha256ByPlatform": ARTIFACT_HASHES,
        "startedUtc": "2026-08-09T13:00:00Z" if replacement else "2026-08-09T12:00:00Z",
        "supersedesRevision": prior,
        "triggerFindingIds": [trigger] if trigger else [],
        "affectedGateIds": ["native-smoke"] if replacement else [],
        "gateRerunEvidencePaths": {"native-smoke": ["evidence/native-smoke.json"]} if replacement else {},
    }


def _session(index: int, revision: str, *, finding_ids: list[str] | None = None) -> dict[str, object]:
    cohort = COHORTS[index % len(COHORTS)][0]
    platform = ARTIFACT_PLATFORMS[index % len(ARTIFACT_PLATFORMS)]
    devices = {
        0: ["keyboard"],
        1: ["xbox-layout-controller"],
        2: ["mouse", "playstation-layout-controller"],
        3: ["keyboard"],
    }[index % 4]
    profiles = ["high-contrast"] if cohort == "accessibility-focused-fresh" else ["default"]
    return {
        "schemaVersion": 1,
        "kind": "vibesnake-external-validation-session-v1",
        "sessionId": f"external-session-{index:03d}",
        "participantId": f"external-{index:03d}",
        "cohortId": cohort,
        "candidateRevision": revision,
        "artifactPlatform": platform,
        "artifactSha256": ARTIFACT_HASHES[platform],
        "appVersion": "0.9.0",
        "cleanInstall": True,
        "neverSeenRepository": cohort != "returning-regression",
        "inputDeviceIds": devices,
        "accessibilityProfileIds": profiles,
        "executedUtc": f"2026-08-09T12:00:{index % 60:02d}Z",
        "distributionId": "controlled-candidate-group-001",
        "consentRecordedSeparately": True,
        "reportFamilyPaths": {family: [f"evidence/session-{index:03d}/{family}.json"] for family in REPORT_FAMILIES},
        "comprehensionResults": [{"checkId": check_id, "result": "pass"} for check_id in COMPREHENSION_CHECKS],
        "crashObserved": False,
        "findingIds": finding_ids or [],
        "evidencePaths": [f"evidence/session-{index:03d}/summary.json"],
    }


def _write_session(path: Path, session: dict[str, object]) -> None:
    _write_json(path, session)
    for paths in session["reportFamilyPaths"].values():
        for relative_path in paths:
            _write_evidence(path.parent / relative_path)
    for relative_path in session["evidencePaths"]:
        _write_evidence(path.parent / relative_path)


def _write_complete_final_sessions(sessions: Path, revision: str) -> None:
    for index in range(4):
        _write_session(sessions / f"session-{index}.json", _session(index, revision))


def _write_empty_findings(path: Path) -> None:
    _write_json(
        path,
        {"schemaVersion": 1, "kind": "vibesnake-external-finding-review-v1", "findings": []},
    )


def test_repository_external_validation_handoff_is_exact_and_pending() -> None:
    errors, evidence = validate_external_validation()

    assert errors == []
    assert evidence["passed"] is True
    assert evidence["protocolQualified"] is True
    assert evidence["artifactPlatformCount"] == 3
    assert evidence["cohortCount"] == 4
    assert evidence["comprehensionCheckCount"] == 6
    assert evidence["reportFamilyCount"] == 4
    assert evidence["candidateCount"] == 0
    assert evidence["sessionCount"] == 0
    assert evidence["externalValidationComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert len(evidence["pendingGates"]) == 5


def test_contract_rejects_a_missing_comprehension_check(tmp_path: Path) -> None:
    contract = copy.deepcopy(json.loads(CONTRACT_PATH.read_text(encoding="utf-8")))
    contract["comprehensionChecks"].remove("another-run-reason")
    path = tmp_path / "contract.json"
    _write_json(path, contract)

    errors, _ = validate_contract(path)

    assert any("contract.comprehensionChecks must be" in error for error in errors)


def test_complete_external_sessions_close_the_gate(tmp_path: Path) -> None:
    sessions = tmp_path / "sessions"
    ledger = tmp_path / "candidate-ledger.json"
    findings = tmp_path / "findings.json"
    revision = "a" * 40
    _write_complete_final_sessions(sessions, revision)
    _write_json(
        ledger,
        {
            "schemaVersion": 1,
            "kind": "vibesnake-external-candidate-ledger-v1",
            "candidates": [_candidate(revision)],
        },
    )
    _write_empty_findings(findings)

    errors, evidence = validate_external_validation(
        sessions_directory=sessions,
        candidate_ledger_path=ledger,
        findings_path=findings,
    )

    assert errors == []
    assert evidence["candidateCount"] == 1
    assert evidence["sessionCount"] == 4
    assert evidence["finalCandidateSessionCount"] == 4
    assert evidence["observedFinalCandidatePlatforms"] == sorted(ARTIFACT_PLATFORMS)
    assert evidence["externalValidationComplete"] is True
    assert evidence["releaseAcceptance"] is True
    assert evidence["pendingGates"] == []


def test_fixed_finding_requires_clean_replacement_and_gate_rerun(tmp_path: Path) -> None:
    sessions = tmp_path / "sessions"
    ledger = tmp_path / "candidate-ledger.json"
    findings = tmp_path / "findings.json"
    first_revision = "a" * 40
    final_revision = "b" * 40
    _write_session(
        sessions / "session-100.json",
        _session(100, first_revision, finding_ids=["EXT-001"]),
    )
    _write_complete_final_sessions(sessions, final_revision)
    _write_evidence(tmp_path / "evidence/native-smoke.json")
    _write_evidence(tmp_path / "evidence/finding-verification.json")
    _write_json(
        ledger,
        {
            "schemaVersion": 1,
            "kind": "vibesnake-external-candidate-ledger-v1",
            "candidates": [
                _candidate(first_revision),
                _candidate(final_revision, prior=first_revision, trigger="EXT-001"),
            ],
        },
    )
    _write_json(
        findings,
        {
            "schemaVersion": 1,
            "kind": "vibesnake-external-finding-review-v1",
            "findings": [
                {
                    "findingId": "EXT-001",
                    "sessionIds": ["external-session-100"],
                    "severity": "P1",
                    "reportFamily": "defect",
                    "affectedGateIds": ["native-smoke"],
                    "decision": "fix",
                    "resolutionStatus": "closed",
                    "workaround": None,
                    "resolutionRevision": final_revision,
                    "verificationEvidencePaths": ["evidence/finding-verification.json"],
                }
            ],
        },
    )

    errors, evidence = validate_external_validation(
        sessions_directory=sessions,
        candidate_ledger_path=ledger,
        findings_path=findings,
    )

    assert errors == []
    assert evidence["candidateCount"] == 2
    assert evidence["findingCount"] == 1
    assert evidence["externalValidationComplete"] is True


def test_missing_evidence_and_failed_fresh_comprehension_block_acceptance(tmp_path: Path) -> None:
    sessions = tmp_path / "sessions"
    ledger = tmp_path / "candidate-ledger.json"
    findings = tmp_path / "findings.json"
    revision = "a" * 40
    _write_complete_final_sessions(sessions, revision)
    first_session_path = sessions / "session-0.json"
    first_session = json.loads(first_session_path.read_text(encoding="utf-8"))
    first_session["comprehensionResults"][0]["result"] = "fail"
    _write_json(first_session_path, first_session)
    (sessions / "evidence/session-000/defect.json").unlink()
    _write_json(
        ledger,
        {
            "schemaVersion": 1,
            "kind": "vibesnake-external-candidate-ledger-v1",
            "candidates": [_candidate(revision)],
        },
    )
    _write_empty_findings(findings)

    errors, evidence = validate_external_validation(
        sessions_directory=sessions,
        candidate_ledger_path=ledger,
        findings_path=findings,
    )

    assert evidence["externalValidationComplete"] is False
    assert evidence["releaseAcceptance"] is False
    assert any("missing retained files" in error for error in errors)
    assert any("fresh participant must pass every comprehension check" in error for error in errors)


def test_malformed_session_fails_closed_without_an_exception(tmp_path: Path) -> None:
    sessions = tmp_path / "sessions"
    ledger = tmp_path / "candidate-ledger.json"
    findings = tmp_path / "findings.json"
    revision = "a" * 40
    _write_complete_final_sessions(sessions, revision)
    malformed_path = sessions / "session-1.json"
    malformed = json.loads(malformed_path.read_text(encoding="utf-8"))
    malformed["inputDeviceIds"] = [{"unexpected": "object"}]
    _write_json(malformed_path, malformed)
    _write_json(
        ledger,
        {
            "schemaVersion": 1,
            "kind": "vibesnake-external-candidate-ledger-v1",
            "candidates": [_candidate(revision)],
        },
    )
    _write_empty_findings(findings)

    errors, evidence = validate_external_validation(
        sessions_directory=sessions,
        candidate_ledger_path=ledger,
        findings_path=findings,
    )

    assert evidence["externalValidationComplete"] is False
    assert any("inputDeviceIds must contain unique supported devices" in error for error in errors)
