"""Contracts for the cross-platform release-matrix aggregate gate."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from scripts.check_release_matrix import PLATFORMS, validate_release_matrix


REVISION = "a" * 40
SMOKE_HASH = "b" * 16
LOCK_HASH = "c" * 64


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def _write_platform(root: Path, platform: str, build_mode: str = "Release") -> None:
    evidence_root = root / f"vibesnake-{platform}-qualification-evidence"
    manifest_path = root / f"vibesnake-{platform}-manifest" / "artifact-manifest.json"
    manifest = {
        "schemaVersion": 2,
        "product": "Vibe Snake",
        "platform": platform,
        "buildMode": build_mode,
        "sourceRevision": REVISION,
        "smokeStateHash": SMOKE_HASH,
        "fileCount": 1,
        "totalBytes": 10,
        "files": [{"path": "player", "bytes": 10, "sha256": "d" * 64}],
    }
    _write_json(manifest_path, manifest)
    manifest_sha = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
    file_hash = "e" * 64
    _write_json(
        evidence_root / "artifact_read_only_install.json",
        {
            "schemaVersion": 1,
            "kind": "artifact-read-only-install-v1",
            "passed": True,
            "platformId": platform,
            "sourceRevision": REVISION,
            "smokeStateHash": SMOKE_HASH,
            "writeProbeRejected": True,
            "installUnchanged": True,
            "userDataOutsideInstall": True,
            "logOutsideInstall": True,
            "evidenceOutsideInstall": True,
            "installPathQualified": True,
            "userDataPathQualified": True,
            "logPathQualified": True,
            "freshProfile": True,
            "beforeSha256": file_hash,
            "afterSha256": file_hash,
        },
    )
    _write_json(
        evidence_root / "dependency_inventory.json",
        {
            "schemaVersion": 1,
            "kind": "dependency-inventory-v1",
            "generatedFromLocksOnly": True,
            "sourceRevision": REVISION,
            "sourceDirty": False,
            "runtimeIdentifier": platform,
            "lockSetSha256": LOCK_HASH,
            "packages": [{"name": "example"}],
        },
    )
    _write_json(
        evidence_root / "release_signing_readiness.json",
        {
            "schemaVersion": 1,
            "kind": "release-signing-readiness-v1",
            "product": "Vibe Snake",
            "platform": platform,
            "sourceRevision": REVISION,
            "buildMode": build_mode,
            "artifactManifestSha256": manifest_sha,
            "signingState": "unsigned-input",
            "passed": True,
            "ordinaryCiCredentialAccess": False,
            "signingMaterialAllowedInRepository": False,
            "signingMaterialAllowedInArtifacts": False,
        },
    )
    _write_json(
        evidence_root / "release_output_plan.json",
        {
            "schemaVersion": 1,
            "kind": "release-output-plan-v1",
            "product": "Vibe Snake",
            "productVersion": "0.2.1",
            "platform": platform,
            "passed": True,
            "qualificationOnly": True,
            "optionalPackOutputSeparate": True,
            "playerDataExcluded": True,
            "uninstallPreservesPlayerData": True,
            "deterministicRepeatMatched": True,
            "publicationEligible": False,
            "baseGameIncludesOptionalPacks": False,
            "packageSha256": "f" * 64,
            "packageBytes": 100,
        },
    )
    resource_samples = [
        {
            "completedRestarts": restart,
            "sceneNodeCount": 9,
            "objectCount": 1683,
            "resourceCount": 2,
            "orphanNodeCount": 0,
        }
        for restart in range(0, 101, 10)
    ]
    _write_json(
        evidence_root / "candidate_reliability.json",
        {
            "schemaVersion": 1,
            "kind": "candidate-reliability-qualification-v1",
            "passed": True,
            "requiredStepsPerRuleset": 100_000,
            "rulesetCount": 2,
            "totalComparedSimulationSteps": 200_000,
            "referenceAiId": "balanced",
            "aiAlgorithmId": "native-personality-controller-v2",
            "randomAlgorithmId": "pcg-xsh-rr-32-v1",
            "simulations": [
                {
                    "modeId": mode_id,
                    "modeVersion": 1,
                    "scoreCategoryId": score_category,
                    "referenceAiId": "balanced",
                    "requiredComparedSteps": 100_000,
                    "comparedSteps": 100_000,
                    "runCount": run_count,
                    "restartCount": run_count - 1,
                    "stateHashCheckpointCount": 180,
                    "decisionsIdentical": True,
                    "queueOutcomesIdentical": True,
                    "stepResultsIdentical": True,
                    "decisionAndStateTraceSha256": "1" * 64,
                    "firstDivergence": None,
                }
                for mode_id, score_category, run_count in (
                    ("classic", "classic-standard-v1", 84),
                    ("vibe", "vibe-standard-v1-dda-on", 82),
                )
            ],
            "spectatorRestarts": {
                "requiredRestarts": 100,
                "completedRestarts": 100,
                "stepsPerRestart": 8,
                "completedSteps": 800,
                "stateResetCount": 100,
                "everyFreshSessionStartedPaused": True,
                "everyFreshSessionResetState": True,
                "everySessionAdvanced": True,
                "managedSessionReferencesRetained": 0,
                "engineNodeCountStable": True,
                "engineObjectCountDidNotGrow": True,
                "engineResourceCountDidNotGrow": True,
                "engineOrphanNodeCountDidNotGrow": True,
                "noMonotonicStateOrResourceGrowth": True,
                "resourceSamples": resource_samples,
            },
            "pendingGates": [
                "retained-release-execution-on-windows-macos-linux",
            ],
        },
    )
    _write_json(
        evidence_root / "candidate_fault_campaign.json",
        {
            "schemaVersion": 1,
            "kind": "candidate-fault-campaign-v1",
            "passed": True,
            "requiredFaultCount": 7,
            "completedFaultCount": 7,
            "everyFaultDetected": True,
            "everyExistingDataBoundaryPreserved": True,
            "everyRecoveryPathVerified": True,
            "rulesStateUnchangedAcrossCampaign": True,
            "faults": [
                {
                    "faultId": fault_id,
                    "injectionBoundary": "production-boundary",
                    "faultDetected": True,
                    "existingDataPreserved": True,
                    "recoveryVerified": True,
                    "rulesStateUnchanged": True,
                }
                for fault_id in (
                    "interrupted-write",
                    "corrupt-json",
                    "full-disk",
                    "read-only-data-directory",
                    "missing-resource",
                    "invalid-content-pack",
                    "unavailable-audio",
                )
            ],
            "crashTriage": {
                "reportKind": "crash-report",
                "reportRetained": True,
                "schemaValid": True,
                "privacySafe": True,
                "reproductionFieldsComplete": True,
                "fileName": "crash.vibesnake-diagnostic.json",
                "sha256": "2" * 64,
            },
            "divergenceTriage": {
                "reportKind": "deterministic-divergence-report-v1",
                "reportRetained": True,
                "schemaValid": True,
                "privacySafe": True,
                "reproductionFieldsComplete": True,
                "fileName": "divergence.vibesnake-divergence.json",
                "sha256": "3" * 64,
            },
            "pendingGates": ["retained-release-execution-on-windows-macos-linux"],
        },
    )
    _write_json(
        evidence_root / "performance.json",
        {
            "schemaVersion": 1,
            "kind": "performance-qualification-v1",
            "passed": True,
            "threeEffectProfilesMeasured": True,
            "maximumMixedStressSceneComplete": True,
            "frameStatisticsComplete": True,
            "sharedHostRegressionCeilingMet": True,
            "particleBudgetConsistent": True,
            "audioChannelBudgetConsistent": True,
            "drawSubmissionBudgetMet": True,
            "feedbackCannotChangeSimulationSpeed": True,
            "rulesStateIdenticalAcrossProfiles": True,
            "finalRulesStateHash": "4" * 16,
            "rulesStepsPerProfile": 256,
            "minimumHardwareAcceptanceStatus": "pending-named-hardware",
            "budget": {
                "targetFramesPerSecond": 60,
                "targetFrameMilliseconds": 1000.0 / 60.0,
                "sharedHostMaximumP95Milliseconds": 50,
                "sharedHostMaximumFrameMilliseconds": 100,
                "maximumLogicalDrawSubmissions": 2400,
                "maximumParticles": 160,
                "maximumAudioChannels": 12,
                "boardCellCapacity": 2112,
                "requiredSamplesPerProfile": 40,
            },
            "profiles": [
                {
                    "id": profile_id,
                    "snakeCellCount": snake_cells,
                    "obstacleCount": obstacles,
                    "visibleCollectibleCount": collectibles,
                    "particleCount": particles,
                    "popupCount": popups,
                    "fullScreenFlashCount": 0,
                    "logicalDrawSubmissionCount": draw_submissions,
                }
                for profile_id, snake_cells, obstacles, collectibles, particles, popups, draw_submissions in (
                    ("minimum", 64, 0, 2, 0, 0, 88),
                    ("default", 512, 3, 2, 64, 2, 610),
                    ("maximum-safe", 2107, 3, 2, 160, 3, 2303),
                )
            ],
            "measurements": [
                {
                    "id": profile_id,
                    "sampleCount": 40,
                    "averageFrameMilliseconds": 7.0,
                    "p50FrameMilliseconds": 7.0,
                    "p95FrameMilliseconds": 8.0,
                    "p99FrameMilliseconds": 9.0,
                    "maximumFrameMilliseconds": 10.0,
                    "driverDrawCallStatus": "unavailable-headless-backend",
                    "averageObservedDriverDrawCalls": 0,
                    "maximumObservedDriverDrawCalls": 0,
                }
                for profile_id in ("minimum", "default", "maximum-safe")
            ],
            "pendingHumanChecks": [
                "Windows named minimum hardware",
                "macOS named minimum hardware",
                "Linux named minimum hardware",
                "Long-session resource and thermal review",
            ],
        },
    )
    accessibility_source_kinds = {
        "accessibility_presentation.json": "accessibility-presentation-v1",
        "shell_presentation.json": "shell-presentation-v1",
        "settings_screen.json": "settings-screen-qualification-v1",
        "input_cadence.json": "input-cadence-qualification-v1",
        "audio_fallback_stress.json": "audio-mixing-policy-v2",
        "multimodal_feedback.json": "multimodal-feedback-v1",
        "viewport_matrix.json": "virtual-viewport-matrix-v1",
    }
    for source_name, source_kind in accessibility_source_kinds.items():
        _write_json(evidence_root / source_name, {"kind": source_kind, "passed": True})
    accessibility_sources = [
        {
            "fileName": source_name,
            "kind": source_kind,
            "sha256": hashlib.sha256((evidence_root / source_name).read_bytes()).hexdigest(),
        }
        for source_name, source_kind in accessibility_source_kinds.items()
    ]
    accessibility_area_ids = (
        "text",
        "contrast",
        "focus",
        "remapping",
        "single-action-navigation",
        "controller-only-use",
        "keyboard-only-use",
        "audio-separation",
        "visual-alternatives",
        "reduced-motion",
        "flash-safety",
        "documentation",
    )
    display_rows = (
        ("minimum-clamp", 320, 180, 640, 360, 0.5),
        ("hd-16-9", 1920, 1080, 1920, 1080, 1.5),
        ("classic-4-3", 1024, 768, 1024, 768, 0.8),
        ("desktop-16-10", 1920, 1200, 1920, 1200, 1.5),
        ("ultrawide-21-9", 3440, 1440, 3440, 1440, 2.0),
        ("square-1-1", 1024, 1024, 1024, 1024, 0.8),
        ("high-density-4k", 3840, 2160, 3840, 2160, 3.0),
        ("high-density-5k", 5120, 2880, 5120, 2880, 4.0),
    )
    _write_json(
        evidence_root / "candidate_accessibility_audit.json",
        {
            "schemaVersion": 1,
            "kind": "candidate-accessibility-audit-v1",
            "passed": True,
            "requiredFlowDefectSeverity": "P1",
            "auditAreaCount": 12,
            "allAutomatedAuditAreasPassed": True,
            "keyboardOnlyRouteComplete": True,
            "controllerOnlyRouteComplete": True,
            "remappingComplete": True,
            "singleActionNavigationComplete": True,
            "independentAudioControlsComplete": True,
            "monoOutputComplete": True,
            "visualAlternativesComplete": True,
            "reducedMotionComplete": True,
            "flashSafetyComplete": True,
            "maximumTextScaleViewportMatrixComplete": True,
            "maximumTextScale": 1.5,
            "supportedDisplayClassCount": 8,
            "maximumTextScaleDisplayClassCount": 8,
            "accessibilityUserReviewStatus": "pending-accessibility-user-review",
            "featureGuidePath": "docs/guides/ACCESSIBILITY.md",
            "featurePublicationStatus": "published-in-repository",
            "auditAreas": [
                {
                    "id": area_id,
                    "automatedPassed": True,
                    "evidenceFiles": ["qualified-source.json"],
                }
                for area_id in accessibility_area_ids
            ],
            "displayClasses": [
                {
                    "id": display_id,
                    "requestedWidth": requested_width,
                    "requestedHeight": requested_height,
                    "effectiveWidth": effective_width,
                    "effectiveHeight": effective_height,
                    "viewportScale": viewport_scale,
                    "textScale": 1.5,
                    "logicalLayoutComplete": True,
                }
                for (
                    display_id,
                    requested_width,
                    requested_height,
                    effective_width,
                    effective_height,
                    viewport_scale,
                ) in display_rows
            ],
            "sources": accessibility_sources,
            "pendingHumanChecks": [
                "retained-visible-audit-windows-macos-linux",
                "maximum-text-scale-platform-captures",
                "physical-keyboard-and-controller-only-flow-review",
                "players-using-relevant-accessibility-settings",
                "human-focus-contrast-readability-photosensitivity-review",
            ],
        },
    )
    if build_mode == "Release":
        _write_json(
            evidence_root / "candidate_launch_reliability.json",
            {
                "schemaVersion": 1,
                "kind": "candidate-launch-reliability-v1",
                "passed": True,
                "platformId": platform,
                "buildMode": "Release",
                "sourceRevision": REVISION,
                "requestedLaunches": 100,
                "completedLaunches": 100,
                "freshProfileLaunches": 100,
                "readOnlyInstall": True,
                "headless": True,
                "failures": [],
            },
        )
        _write_json(
            evidence_root / "candidate_install_lifecycle.json",
            {
                "schemaVersion": 1,
                "kind": "candidate-install-lifecycle-preflight-v1",
                "passed": True,
                "platformId": platform,
                "buildMode": "Release",
                "sourceRevision": REVISION,
                "firstInstallPassed": True,
                "readOnlyInstallPassed": True,
                "noElevationRequested": True,
                "nonAsciiInstallAndUserPathsPassed": True,
                "repairSnapshotMatched": True,
                "repairLaunchPassed": True,
                "preferenceMigrationFixtureCount": 6,
                "preferenceMigrations": [
                    {
                        "inputSchema": schema,
                        "effectiveSchema": 7,
                        "loadCode": "Success",
                        "sourcePreserved": True,
                    }
                    for schema in range(1, 7)
                ],
                "additionalSaveMigrationFixtureCount": 2,
                "additionalSaveMigrations": [
                    {
                        "fixture": "personal-best-schema-1",
                        "effectiveSchema": 2,
                        "loadCode": "Success",
                        "sourcePreserved": True,
                    },
                    {
                        "fixture": "local-playtest-summary-schema-1",
                        "effectiveSchema": 2,
                        "loadCode": "Success",
                        "sourcePreserved": True,
                    },
                ],
                "supportedSaveMigrationFixtureCount": 8,
                "futureSchemaRejectedAndPreserved": True,
                "rollbackNeverOverwritesNewerPreferences": True,
                "optionalPackAddRemovalRestorePassed": True,
                "dataResetBackupRestorePassed": True,
                "applicationRemovalPreservedPlayerData": True,
                "completeSupportedSaveFixtureMatrix": True,
                "remainingGates": [
                    "selected-channel-installer-lifecycle",
                    "cross-version-binary-rollback",
                ],
            },
        )


def _write_matrix(root: Path, build_mode: str = "Release") -> None:
    for platform in PLATFORMS:
        _write_platform(root, platform, build_mode)


def test_complete_release_matrix_cross_binds_all_platform_evidence(tmp_path: Path) -> None:
    _write_matrix(tmp_path)

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert errors == []
    assert evidence["passed"] is True
    assert [row["platform"] for row in evidence["platforms"]] == list(PLATFORMS)
    assert evidence["sharedSmokeStateHash"] == SMOKE_HASH
    assert evidence["sharedLockSetSha256"] == LOCK_HASH
    assert evidence["totalCleanLaunches"] == 300
    assert evidence["installLifecyclePreflightPlatforms"] == 3
    assert evidence["totalSupportedSaveMigrationFixtures"] == 24
    assert evidence["totalReliabilityComparedSteps"] == 600_000
    assert evidence["totalSpectatorRestarts"] == 300
    assert evidence["totalInjectedFaults"] == 21
    assert evidence["crashTriagePlatforms"] == 3
    assert evidence["divergenceTriagePlatforms"] == 3
    assert evidence["totalPerformanceSamples"] == 360
    assert evidence["maximumSharedHostP99Milliseconds"] == 9.0
    assert evidence["sharedPerformanceRulesStateHash"] == "4" * 16
    assert evidence["accessibilityAuditPlatforms"] == 3
    assert evidence["totalMaximumTextScaleDisplayClasses"] == 24
    assert evidence["sharedReliabilityTraceSha256ByMode"] == {
        "classic": "1" * 64,
        "vibe": "1" * 64,
    }
    assert "remaining-fault-injection-and-triage" not in evidence["remainingProtectedOperations"]
    assert "retained-accessibility-audit-and-user-review" in evidence["remainingProtectedOperations"]
    assert evidence["publicationEligible"] is False


def test_debug_matrix_does_not_claim_or_require_candidate_launches(tmp_path: Path) -> None:
    _write_matrix(tmp_path, "Debug")

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Debug")

    assert errors == []
    assert evidence["passed"] is True
    assert evidence["totalCleanLaunches"] == 0


def test_release_matrix_rejects_missing_platform_evidence(tmp_path: Path) -> None:
    _write_matrix(tmp_path)
    missing = tmp_path / "vibesnake-linux-x64-qualification-evidence" / "artifact_read_only_install.json"
    missing.unlink()

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert evidence["passed"] is False
    assert any("missing linux-x64 readOnly evidence" in error for error in errors)
    assert any("exactly 3 complete platform rows" in error for error in errors)


def test_release_matrix_rejects_dirty_or_mismatched_source_identity(tmp_path: Path) -> None:
    _write_matrix(tmp_path)
    path = tmp_path / "vibesnake-windows-x64-qualification-evidence" / "dependency_inventory.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    document["sourceDirty"] = True
    _write_json(path, document)

    errors, evidence = validate_release_matrix(tmp_path, "1" * 40, "Release")

    assert evidence["passed"] is False
    assert any("sourceDirty must be False" in error for error in errors)
    assert any("sourceRevision must be" in error for error in errors)


def test_release_matrix_rejects_cross_platform_hash_drift(tmp_path: Path) -> None:
    _write_matrix(tmp_path)
    path = tmp_path / "vibesnake-macos-universal-qualification-evidence" / "dependency_inventory.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    document["lockSetSha256"] = "1" * 64
    _write_json(path, document)

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert evidence["passed"] is False
    assert "all platform dependency inventories must report one lock-set SHA-256" in errors
    assert evidence["sharedLockSetSha256"] is None


def test_release_matrix_rejects_missing_candidate_lifecycle_evidence(tmp_path: Path) -> None:
    _write_matrix(tmp_path)
    missing = tmp_path / "vibesnake-linux-x64-qualification-evidence" / "candidate_install_lifecycle.json"
    missing.unlink()

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert evidence["passed"] is False
    assert any("missing linux-x64 candidate lifecycle evidence" in error for error in errors)
    assert any("exactly 3 complete platform rows" in error for error in errors)


def test_release_matrix_rejects_incomplete_save_migration_matrix(tmp_path: Path) -> None:
    _write_matrix(tmp_path)
    path = tmp_path / "vibesnake-windows-x64-qualification-evidence" / "candidate_install_lifecycle.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    document["completeSupportedSaveFixtureMatrix"] = False
    document["preferenceMigrations"] = document["preferenceMigrations"][:-1]
    _write_json(path, document)

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert evidence["passed"] is False
    assert any("completeSupportedSaveFixtureMatrix must be True" in error for error in errors)
    assert any("must cover schemas 1 through 6" in error for error in errors)


def test_release_matrix_rejects_reliability_drift_or_resource_growth(tmp_path: Path) -> None:
    _write_matrix(tmp_path)
    path = tmp_path / "vibesnake-macos-universal-qualification-evidence" / "candidate_reliability.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    document["simulations"][1]["stepResultsIdentical"] = False
    document["simulations"][1]["decisionAndStateTraceSha256"] = "2" * 64
    document["spectatorRestarts"]["resourceSamples"][-1]["objectCount"] += 1
    _write_json(path, document)

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert evidence["passed"] is False
    assert any("stepResultsIdentical must be True" in error for error in errors)
    assert any("resources grew across restart samples" in error for error in errors)
    assert any("one vibe trace SHA-256" in error for error in errors)


def test_release_matrix_rejects_incomplete_fault_or_triage_evidence(tmp_path: Path) -> None:
    _write_matrix(tmp_path)
    path = tmp_path / "vibesnake-linux-x64-qualification-evidence" / "candidate_fault_campaign.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    document["faults"][2]["faultDetected"] = False
    document["divergenceTriage"]["privacySafe"] = False
    document["crashTriage"]["sha256"] = "bad"
    _write_json(path, document)

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert evidence["passed"] is False
    assert any("faultDetected must be True" in error for error in errors)
    assert any("privacySafe must be True" in error for error in errors)
    assert any("crashTriage.sha256 must be a SHA-256 digest" in error for error in errors)


def test_release_matrix_rejects_performance_drift_or_shared_host_regression(
    tmp_path: Path,
) -> None:
    _write_matrix(tmp_path)
    path = tmp_path / "vibesnake-windows-x64-qualification-evidence" / "performance.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    document["profiles"][2]["particleCount"] = 161
    document["measurements"][2]["p99FrameMilliseconds"] = 101.0
    document["measurements"][2]["maximumFrameMilliseconds"] = 101.0
    document["finalRulesStateHash"] = "5" * 16
    _write_json(path, document)

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert evidence["passed"] is False
    assert any("stress shape drifted" in error for error in errors)
    assert any("exceeded the shared-host ceiling" in error for error in errors)
    assert any("one rules state hash" in error for error in errors)


def test_release_matrix_rejects_accessibility_drift_or_unbound_source(tmp_path: Path) -> None:
    _write_matrix(tmp_path)
    path = tmp_path / "vibesnake-macos-universal-qualification-evidence" / "candidate_accessibility_audit.json"
    document = json.loads(path.read_text(encoding="utf-8"))
    document["auditAreas"][3]["automatedPassed"] = False
    document["displayClasses"][2]["textScale"] = 1.0
    document["sources"][0]["sha256"] = "0" * 64
    _write_json(path, document)

    errors, evidence = validate_release_matrix(tmp_path, REVISION, "Release")

    assert evidence["passed"] is False
    assert any("automatedPassed must be True" in error for error in errors)
    assert any("textScale must be 1.5" in error for error in errors)
    assert any("sha256 does not match accessibility_presentation.json" in error for error in errors)
