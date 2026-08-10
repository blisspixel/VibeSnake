"""Cross-bind the three native artifact jobs into one release-matrix decision."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


PLATFORMS = ("windows-x64", "macos-universal", "linux-x64")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")
REVISION_PATTERN = re.compile(r"[0-9a-f]{40}")
STATE_HASH_PATTERN = re.compile(r"[0-9a-f]{16}")
EVIDENCE_FILES = {
    "readOnly": "artifact_read_only_install.json",
    "dependencies": "dependency_inventory.json",
    "signing": "release_signing_readiness.json",
    "output": "release_output_plan.json",
    "reliability": "candidate_reliability.json",
    "faults": "candidate_fault_campaign.json",
    "performance": "performance.json",
    "accessibility": "candidate_accessibility_audit.json",
}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _read_json(path: Path, label: str, errors: list[str]) -> Any | None:
    if not path.is_file():
        errors.append(f"missing {label}: {path}")
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"unreadable {label}: {path}: {exc}")
        return None


def _expect(
    document: dict[str, Any],
    field: str,
    expected: Any,
    label: str,
    errors: list[str],
) -> None:
    actual = document.get(field)
    if actual != expected:
        errors.append(f"{label}.{field} must be {expected!r}; got {actual!r}")


def _expect_true_fields(document: dict[str, Any], fields: tuple[str, ...], label: str, errors: list[str]) -> None:
    for field in fields:
        _expect(document, field, True, label, errors)


def _platform_paths(root: Path, platform: str) -> tuple[Path, Path]:
    evidence = root / f"vibesnake-{platform}-qualification-evidence"
    manifest = root / f"vibesnake-{platform}-manifest" / "artifact-manifest.json"
    return evidence, manifest


def validate_release_matrix(
    root: Path, expected_revision: str, expected_build_mode: str
) -> tuple[list[str], dict[str, Any]]:
    """Validate downloaded CI artifacts and return closed aggregate evidence."""
    errors: list[str] = []
    if not REVISION_PATTERN.fullmatch(expected_revision):
        errors.append("expected revision must be a lowercase 40-character Git revision")
    if expected_build_mode not in {"Debug", "Release"}:
        errors.append("expected build mode must be Debug or Release")

    rows: list[dict[str, Any]] = []
    smoke_hashes: set[str] = set()
    lock_hashes: set[str] = set()
    product_versions: set[str] = set()
    reliability_trace_hashes: dict[str, set[str]] = {
        "classic": set(),
        "vibe": set(),
    }
    performance_rules_hashes: set[str] = set()
    for platform in PLATFORMS:
        evidence_root, manifest_path = _platform_paths(root, platform)
        manifest = _read_json(manifest_path, f"{platform} artifact manifest", errors)
        documents = {
            name: _read_json(evidence_root / filename, f"{platform} {name} evidence", errors)
            for name, filename in EVIDENCE_FILES.items()
        }
        if expected_build_mode == "Release":
            documents["launches"] = _read_json(
                evidence_root / "candidate_launch_reliability.json",
                f"{platform} candidate launch evidence",
                errors,
            )
            documents["lifecycle"] = _read_json(
                evidence_root / "candidate_install_lifecycle.json",
                f"{platform} candidate lifecycle evidence",
                errors,
            )
        if not isinstance(manifest, dict) or not all(isinstance(document, dict) for document in documents.values()):
            continue

        read_only = documents["readOnly"]
        dependencies = documents["dependencies"]
        signing = documents["signing"]
        output = documents["output"]
        reliability = documents["reliability"]
        faults = documents["faults"]
        performance = documents["performance"]
        accessibility = documents["accessibility"]
        launches = documents.get("launches")
        lifecycle = documents.get("lifecycle")
        manifest_sha = _sha256(manifest_path)

        _expect(manifest, "schemaVersion", 2, f"{platform} manifest", errors)
        _expect(manifest, "product", "Vibe Snake", f"{platform} manifest", errors)
        _expect(manifest, "platform", platform, f"{platform} manifest", errors)
        _expect(manifest, "buildMode", expected_build_mode, f"{platform} manifest", errors)
        _expect(manifest, "sourceRevision", expected_revision, f"{platform} manifest", errors)
        smoke_hash = manifest.get("smokeStateHash")
        if not STATE_HASH_PATTERN.fullmatch(str(smoke_hash)):
            errors.append(f"{platform} manifest.smokeStateHash must be 16 lowercase hex characters")
        else:
            smoke_hashes.add(smoke_hash)
        if not isinstance(manifest.get("files"), list) or not manifest["files"]:
            errors.append(f"{platform} manifest.files must be a nonempty array")
        if manifest.get("fileCount") != len(manifest.get("files", [])):
            errors.append(f"{platform} manifest.fileCount must match files")

        _expect(read_only, "schemaVersion", 1, f"{platform} readOnly", errors)
        _expect(read_only, "kind", "artifact-read-only-install-v1", f"{platform} readOnly", errors)
        _expect(read_only, "platformId", platform, f"{platform} readOnly", errors)
        _expect(read_only, "sourceRevision", expected_revision, f"{platform} readOnly", errors)
        _expect(read_only, "smokeStateHash", smoke_hash, f"{platform} readOnly", errors)
        _expect_true_fields(
            read_only,
            (
                "passed",
                "writeProbeRejected",
                "installUnchanged",
                "userDataOutsideInstall",
                "logOutsideInstall",
                "evidenceOutsideInstall",
                "installPathQualified",
                "userDataPathQualified",
                "logPathQualified",
                "freshProfile",
            ),
            f"{platform} readOnly",
            errors,
        )
        if read_only.get("beforeSha256") != read_only.get("afterSha256"):
            errors.append(f"{platform} readOnly install hashes must match")

        _expect(dependencies, "schemaVersion", 1, f"{platform} dependencies", errors)
        _expect(
            dependencies,
            "kind",
            "dependency-inventory-v1",
            f"{platform} dependencies",
            errors,
        )
        _expect(dependencies, "generatedFromLocksOnly", True, f"{platform} dependencies", errors)
        _expect(dependencies, "sourceRevision", expected_revision, f"{platform} dependencies", errors)
        _expect(dependencies, "sourceDirty", False, f"{platform} dependencies", errors)
        lock_hash = dependencies.get("lockSetSha256")
        if not SHA256_PATTERN.fullmatch(str(lock_hash)):
            errors.append(f"{platform} dependencies.lockSetSha256 must be a SHA-256 digest")
        else:
            lock_hashes.add(lock_hash)
        if not isinstance(dependencies.get("packages"), list) or not dependencies["packages"]:
            errors.append(f"{platform} dependencies.packages must be a nonempty array")

        _expect(signing, "schemaVersion", 1, f"{platform} signing", errors)
        _expect(signing, "kind", "release-signing-readiness-v1", f"{platform} signing", errors)
        _expect(signing, "product", "Vibe Snake", f"{platform} signing", errors)
        _expect(signing, "platform", platform, f"{platform} signing", errors)
        _expect(signing, "sourceRevision", expected_revision, f"{platform} signing", errors)
        _expect(signing, "buildMode", expected_build_mode, f"{platform} signing", errors)
        _expect(signing, "artifactManifestSha256", manifest_sha, f"{platform} signing", errors)
        _expect(signing, "signingState", "unsigned-input", f"{platform} signing", errors)
        _expect(signing, "passed", True, f"{platform} signing", errors)
        _expect(signing, "ordinaryCiCredentialAccess", False, f"{platform} signing", errors)
        _expect(signing, "signingMaterialAllowedInRepository", False, f"{platform} signing", errors)
        _expect(signing, "signingMaterialAllowedInArtifacts", False, f"{platform} signing", errors)

        _expect(output, "schemaVersion", 1, f"{platform} output", errors)
        _expect(output, "kind", "release-output-plan-v1", f"{platform} output", errors)
        _expect(output, "product", "Vibe Snake", f"{platform} output", errors)
        _expect(output, "platform", platform, f"{platform} output", errors)
        _expect_true_fields(
            output,
            (
                "passed",
                "qualificationOnly",
                "optionalPackOutputSeparate",
                "playerDataExcluded",
                "uninstallPreservesPlayerData",
                "deterministicRepeatMatched",
            ),
            f"{platform} output",
            errors,
        )
        _expect(output, "publicationEligible", False, f"{platform} output", errors)
        _expect(output, "baseGameIncludesOptionalPacks", False, f"{platform} output", errors)
        package_sha = output.get("packageSha256")
        if not SHA256_PATTERN.fullmatch(str(package_sha)):
            errors.append(f"{platform} output.packageSha256 must be a SHA-256 digest")
        if not isinstance(output.get("packageBytes"), int) or output["packageBytes"] <= 0:
            errors.append(f"{platform} output.packageBytes must be a positive integer")
        product_version = output.get("productVersion")
        if not isinstance(product_version, str) or not product_version:
            errors.append(f"{platform} output.productVersion must be nonempty")
        else:
            product_versions.add(product_version)

        _expect(reliability, "schemaVersion", 1, f"{platform} reliability", errors)
        _expect(
            reliability,
            "kind",
            "candidate-reliability-qualification-v1",
            f"{platform} reliability",
            errors,
        )
        _expect(reliability, "passed", True, f"{platform} reliability", errors)
        _expect(
            reliability,
            "requiredStepsPerRuleset",
            100_000,
            f"{platform} reliability",
            errors,
        )
        _expect(reliability, "rulesetCount", 2, f"{platform} reliability", errors)
        _expect(
            reliability,
            "totalComparedSimulationSteps",
            200_000,
            f"{platform} reliability",
            errors,
        )
        _expect(reliability, "referenceAiId", "balanced", f"{platform} reliability", errors)
        _expect(
            reliability,
            "aiAlgorithmId",
            "native-personality-controller-v2",
            f"{platform} reliability",
            errors,
        )
        _expect(
            reliability,
            "randomAlgorithmId",
            "pcg-xsh-rr-32-v1",
            f"{platform} reliability",
            errors,
        )
        simulations = reliability.get("simulations")
        expected_modes = {
            "classic": "classic-standard-v1",
            "vibe": "vibe-standard-v1-dda-on",
        }
        if not isinstance(simulations, list) or len(simulations) != 2:
            errors.append(f"{platform} reliability.simulations must contain two ruleset rows")
        else:
            observed_modes: set[str] = set()
            for index, simulation in enumerate(simulations):
                label = f"{platform} reliability.simulations[{index}]"
                if not isinstance(simulation, dict):
                    errors.append(f"{label} must be an object")
                    continue
                mode_id = simulation.get("modeId")
                observed_modes.add(str(mode_id))
                _expect(simulation, "modeVersion", 1, label, errors)
                _expect(
                    simulation,
                    "scoreCategoryId",
                    expected_modes.get(str(mode_id)),
                    label,
                    errors,
                )
                _expect(simulation, "referenceAiId", "balanced", label, errors)
                _expect(simulation, "requiredComparedSteps", 100_000, label, errors)
                _expect(simulation, "comparedSteps", 100_000, label, errors)
                _expect_true_fields(
                    simulation,
                    ("decisionsIdentical", "queueOutcomesIdentical", "stepResultsIdentical"),
                    label,
                    errors,
                )
                _expect(simulation, "firstDivergence", None, label, errors)
                run_count = simulation.get("runCount")
                if not isinstance(run_count, int) or run_count <= 0:
                    errors.append(f"{label}.runCount must be a positive integer")
                elif simulation.get("restartCount") != run_count - 1:
                    errors.append(f"{label}.restartCount must equal runCount minus one")
                checkpoint_count = simulation.get("stateHashCheckpointCount")
                if not isinstance(checkpoint_count, int) or checkpoint_count < 100:
                    errors.append(f"{label}.stateHashCheckpointCount must be at least 100")
                trace_sha = simulation.get("decisionAndStateTraceSha256")
                if not SHA256_PATTERN.fullmatch(str(trace_sha)):
                    errors.append(f"{label}.decisionAndStateTraceSha256 must be a SHA-256 digest")
                elif str(mode_id) in reliability_trace_hashes:
                    reliability_trace_hashes[str(mode_id)].add(trace_sha)
            if observed_modes != set(expected_modes):
                errors.append(f"{platform} reliability simulations must cover classic and vibe")

        spectator_restarts = reliability.get("spectatorRestarts")
        if not isinstance(spectator_restarts, dict):
            errors.append(f"{platform} reliability.spectatorRestarts must be an object")
        else:
            spectator_label = f"{platform} reliability.spectatorRestarts"
            _expect(spectator_restarts, "requiredRestarts", 100, spectator_label, errors)
            _expect(spectator_restarts, "completedRestarts", 100, spectator_label, errors)
            _expect(spectator_restarts, "stepsPerRestart", 8, spectator_label, errors)
            _expect(spectator_restarts, "completedSteps", 800, spectator_label, errors)
            _expect(spectator_restarts, "stateResetCount", 100, spectator_label, errors)
            _expect(
                spectator_restarts,
                "managedSessionReferencesRetained",
                0,
                spectator_label,
                errors,
            )
            _expect_true_fields(
                spectator_restarts,
                (
                    "everyFreshSessionStartedPaused",
                    "everyFreshSessionResetState",
                    "everySessionAdvanced",
                    "engineNodeCountStable",
                    "engineObjectCountDidNotGrow",
                    "engineResourceCountDidNotGrow",
                    "engineOrphanNodeCountDidNotGrow",
                    "noMonotonicStateOrResourceGrowth",
                ),
                spectator_label,
                errors,
            )
            resource_samples = spectator_restarts.get("resourceSamples")
            expected_restarts = list(range(0, 101, 10))
            if not isinstance(resource_samples, list) or len(resource_samples) != 11:
                errors.append(f"{spectator_label}.resourceSamples must contain eleven samples")
            elif not all(isinstance(sample, dict) for sample in resource_samples):
                errors.append(f"{spectator_label}.resourceSamples must contain objects")
            elif [sample.get("completedRestarts") for sample in resource_samples] != expected_restarts:
                errors.append(f"{spectator_label}.resourceSamples cadence must be 0 through 100 by ten")
            else:
                baseline = resource_samples[0]
                count_fields = (
                    "sceneNodeCount",
                    "objectCount",
                    "resourceCount",
                    "orphanNodeCount",
                )
                counts_valid = True
                for sample_index, sample in enumerate(resource_samples):
                    for field in count_fields:
                        value = sample.get(field)
                        if type(value) is not int or value < 0:
                            errors.append(
                                f"{spectator_label}.resourceSamples[{sample_index}].{field} "
                                "must be a nonnegative integer"
                            )
                            counts_valid = False
                if counts_valid:
                    for sample in resource_samples:
                        if (
                            sample["sceneNodeCount"] != baseline["sceneNodeCount"]
                            or sample["objectCount"] > baseline["objectCount"]
                            or sample["resourceCount"] > baseline["resourceCount"]
                            or sample["orphanNodeCount"] > baseline["orphanNodeCount"]
                        ):
                            errors.append(f"{spectator_label} resources grew across restart samples")
                            break

        _expect(
            reliability,
            "pendingGates",
            ["retained-release-execution-on-windows-macos-linux"],
            f"{platform} reliability",
            errors,
        )

        fault_label = f"{platform} faults"
        _expect(faults, "schemaVersion", 1, fault_label, errors)
        _expect(faults, "kind", "candidate-fault-campaign-v1", fault_label, errors)
        _expect(faults, "passed", True, fault_label, errors)
        _expect(faults, "requiredFaultCount", 7, fault_label, errors)
        _expect(faults, "completedFaultCount", 7, fault_label, errors)
        _expect_true_fields(
            faults,
            (
                "everyFaultDetected",
                "everyExistingDataBoundaryPreserved",
                "everyRecoveryPathVerified",
                "rulesStateUnchangedAcrossCampaign",
            ),
            fault_label,
            errors,
        )
        expected_fault_ids = [
            "interrupted-write",
            "corrupt-json",
            "full-disk",
            "read-only-data-directory",
            "missing-resource",
            "invalid-content-pack",
            "unavailable-audio",
        ]
        fault_rows = faults.get("faults")
        if not isinstance(fault_rows, list) or len(fault_rows) != 7:
            errors.append(f"{fault_label}.faults must contain seven rows")
        elif not all(isinstance(row, dict) for row in fault_rows):
            errors.append(f"{fault_label}.faults must contain objects")
        else:
            if [row.get("faultId") for row in fault_rows] != expected_fault_ids:
                errors.append(f"{fault_label}.faults must cover the exact roadmap fault order")
            for index, row in enumerate(fault_rows):
                row_label = f"{fault_label}.faults[{index}]"
                _expect_true_fields(
                    row,
                    (
                        "faultDetected",
                        "existingDataPreserved",
                        "recoveryVerified",
                        "rulesStateUnchanged",
                    ),
                    row_label,
                    errors,
                )
                if not isinstance(row.get("injectionBoundary"), str) or not row["injectionBoundary"]:
                    errors.append(f"{row_label}.injectionBoundary must be nonempty")

        expected_triage_kinds = {
            "crashTriage": "crash-report",
            "divergenceTriage": "deterministic-divergence-report-v1",
        }
        for field, expected_kind in expected_triage_kinds.items():
            triage = faults.get(field)
            triage_label = f"{fault_label}.{field}"
            if not isinstance(triage, dict):
                errors.append(f"{triage_label} must be an object")
                continue
            _expect(triage, "reportKind", expected_kind, triage_label, errors)
            _expect_true_fields(
                triage,
                (
                    "reportRetained",
                    "schemaValid",
                    "privacySafe",
                    "reproductionFieldsComplete",
                ),
                triage_label,
                errors,
            )
            file_name = triage.get("fileName")
            if not isinstance(file_name, str) or not file_name or Path(file_name).name != file_name:
                errors.append(f"{triage_label}.fileName must be a local base name")
            if not SHA256_PATTERN.fullmatch(str(triage.get("sha256"))):
                errors.append(f"{triage_label}.sha256 must be a SHA-256 digest")
        _expect(
            faults,
            "pendingGates",
            ["retained-release-execution-on-windows-macos-linux"],
            fault_label,
            errors,
        )

        performance_label = f"{platform} performance"
        _expect(performance, "schemaVersion", 1, performance_label, errors)
        _expect(
            performance,
            "kind",
            "performance-qualification-v1",
            performance_label,
            errors,
        )
        _expect(performance, "passed", True, performance_label, errors)
        _expect_true_fields(
            performance,
            (
                "threeEffectProfilesMeasured",
                "maximumMixedStressSceneComplete",
                "frameStatisticsComplete",
                "sharedHostRegressionCeilingMet",
                "particleBudgetConsistent",
                "audioChannelBudgetConsistent",
                "drawSubmissionBudgetMet",
                "feedbackCannotChangeSimulationSpeed",
                "rulesStateIdenticalAcrossProfiles",
            ),
            performance_label,
            errors,
        )
        _expect(performance, "rulesStepsPerProfile", 256, performance_label, errors)
        _expect(
            performance,
            "minimumHardwareAcceptanceStatus",
            "pending-named-hardware",
            performance_label,
            errors,
        )
        performance_rules_hash = performance.get("finalRulesStateHash")
        if not STATE_HASH_PATTERN.fullmatch(str(performance_rules_hash)):
            errors.append(f"{performance_label}.finalRulesStateHash must be 16 lowercase hex")
        else:
            performance_rules_hashes.add(performance_rules_hash)
        budget = performance.get("budget")
        expected_budget = {
            "targetFramesPerSecond": 60,
            "sharedHostMaximumP95Milliseconds": 50,
            "sharedHostMaximumFrameMilliseconds": 100,
            "maximumLogicalDrawSubmissions": 2400,
            "maximumParticles": 160,
            "maximumAudioChannels": 12,
            "boardCellCapacity": 2112,
            "requiredSamplesPerProfile": 40,
        }
        if not isinstance(budget, dict):
            errors.append(f"{performance_label}.budget must be an object")
            budget = {}
        else:
            for field, expected in expected_budget.items():
                _expect(budget, field, expected, f"{performance_label}.budget", errors)
            target_frame_ms = budget.get("targetFrameMilliseconds")
            if (
                not isinstance(target_frame_ms, (int, float))
                or isinstance(target_frame_ms, bool)
                or abs(target_frame_ms - (1000.0 / 60.0)) > 0.001
            ):
                errors.append(f"{performance_label}.budget.targetFrameMilliseconds must match 60 FPS")

        expected_profile_ids = ["minimum", "default", "maximum-safe"]
        profiles = performance.get("profiles")
        expected_profile_shapes = {
            "minimum": (64, 0, 2, 0, 0, 0, 88),
            "default": (512, 3, 2, 64, 2, 0, 610),
            "maximum-safe": (2107, 3, 2, 160, 3, 0, 2303),
        }
        if not isinstance(profiles, list) or len(profiles) != 3:
            errors.append(f"{performance_label}.profiles must contain three rows")
        elif not all(isinstance(profile, dict) for profile in profiles):
            errors.append(f"{performance_label}.profiles must contain objects")
        else:
            if [profile.get("id") for profile in profiles] != expected_profile_ids:
                errors.append(f"{performance_label}.profiles must use the exact effect order")
            for index, profile in enumerate(profiles):
                profile_id = str(profile.get("id"))
                expected_shape = expected_profile_shapes.get(profile_id)
                actual_shape = (
                    profile.get("snakeCellCount"),
                    profile.get("obstacleCount"),
                    profile.get("visibleCollectibleCount"),
                    profile.get("particleCount"),
                    profile.get("popupCount"),
                    profile.get("fullScreenFlashCount"),
                    profile.get("logicalDrawSubmissionCount"),
                )
                if actual_shape != expected_shape:
                    errors.append(f"{performance_label}.profiles[{index}] stress shape drifted")

        measurements = performance.get("measurements")
        performance_sample_count = 0
        maximum_p99 = 0.0
        if not isinstance(measurements, list) or len(measurements) != 3:
            errors.append(f"{performance_label}.measurements must contain three rows")
        elif not all(isinstance(measurement, dict) for measurement in measurements):
            errors.append(f"{performance_label}.measurements must contain objects")
        else:
            if [measurement.get("id") for measurement in measurements] != expected_profile_ids:
                errors.append(f"{performance_label}.measurements must use the exact effect order")
            for index, measurement in enumerate(measurements):
                measurement_label = f"{performance_label}.measurements[{index}]"
                sample_count = measurement.get("sampleCount")
                if type(sample_count) is not int or sample_count < 40:
                    errors.append(f"{measurement_label}.sampleCount must be at least 40")
                    continue
                performance_sample_count += sample_count
                timing_fields = (
                    "averageFrameMilliseconds",
                    "p50FrameMilliseconds",
                    "p95FrameMilliseconds",
                    "p99FrameMilliseconds",
                    "maximumFrameMilliseconds",
                )
                timings = [measurement.get(field) for field in timing_fields]
                if not all(
                    isinstance(value, (int, float)) and not isinstance(value, bool) and value > 0 for value in timings
                ):
                    errors.append(f"{measurement_label} frame timings must be positive numbers")
                    continue
                _, p50, p95, p99, maximum = timings
                maximum_p99 = max(maximum_p99, p99)
                if not (p50 <= p95 <= p99 <= maximum):
                    errors.append(f"{measurement_label} percentile ordering is invalid")
                if p95 > 50 or maximum > 100:
                    errors.append(f"{measurement_label} exceeded the shared-host ceiling")
                if measurement.get("driverDrawCallStatus") not in {
                    "observed",
                    "unavailable-headless-backend",
                }:
                    errors.append(f"{measurement_label}.driverDrawCallStatus is invalid")
        pending_performance = performance.get("pendingHumanChecks")
        if (
            not isinstance(pending_performance, list)
            or len(pending_performance) != 4
            or not all(isinstance(item, str) and item for item in pending_performance)
        ):
            errors.append(f"{performance_label}.pendingHumanChecks must contain four checks")

        accessibility_label = f"{platform} accessibility"
        _expect(accessibility, "schemaVersion", 1, accessibility_label, errors)
        _expect(
            accessibility,
            "kind",
            "candidate-accessibility-audit-v1",
            accessibility_label,
            errors,
        )
        _expect(accessibility, "passed", True, accessibility_label, errors)
        _expect(
            accessibility,
            "requiredFlowDefectSeverity",
            "P1",
            accessibility_label,
            errors,
        )
        _expect(accessibility, "auditAreaCount", 12, accessibility_label, errors)
        _expect_true_fields(
            accessibility,
            (
                "allAutomatedAuditAreasPassed",
                "keyboardOnlyRouteComplete",
                "controllerOnlyRouteComplete",
                "remappingComplete",
                "singleActionNavigationComplete",
                "independentAudioControlsComplete",
                "monoOutputComplete",
                "visualAlternativesComplete",
                "reducedMotionComplete",
                "flashSafetyComplete",
                "maximumTextScaleViewportMatrixComplete",
            ),
            accessibility_label,
            errors,
        )
        _expect(accessibility, "maximumTextScale", 1.5, accessibility_label, errors)
        _expect(accessibility, "supportedDisplayClassCount", 8, accessibility_label, errors)
        _expect(
            accessibility,
            "maximumTextScaleDisplayClassCount",
            8,
            accessibility_label,
            errors,
        )
        _expect(
            accessibility,
            "accessibilityUserReviewStatus",
            "pending-accessibility-user-review",
            accessibility_label,
            errors,
        )
        _expect(
            accessibility,
            "featureGuidePath",
            "docs/guides/ACCESSIBILITY.md",
            accessibility_label,
            errors,
        )
        _expect(
            accessibility,
            "featurePublicationStatus",
            "published-in-repository",
            accessibility_label,
            errors,
        )
        expected_accessibility_areas = [
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
        ]
        audit_areas = accessibility.get("auditAreas")
        if not isinstance(audit_areas, list) or len(audit_areas) != 12:
            errors.append(f"{accessibility_label}.auditAreas must contain twelve rows")
        elif not all(isinstance(area, dict) for area in audit_areas):
            errors.append(f"{accessibility_label}.auditAreas must contain objects")
        else:
            if [area.get("id") for area in audit_areas] != expected_accessibility_areas:
                errors.append(f"{accessibility_label}.auditAreas must use the exact roadmap order")
            for index, area in enumerate(audit_areas):
                area_label = f"{accessibility_label}.auditAreas[{index}]"
                _expect(area, "automatedPassed", True, area_label, errors)
                evidence_files = area.get("evidenceFiles")
                if (
                    not isinstance(evidence_files, list)
                    or not evidence_files
                    or not all(isinstance(item, str) and item for item in evidence_files)
                ):
                    errors.append(f"{area_label}.evidenceFiles must be a nonempty string array")

        expected_display_rows = [
            ("minimum-clamp", 320, 180, 640, 360),
            ("hd-16-9", 1920, 1080, 1920, 1080),
            ("classic-4-3", 1024, 768, 1024, 768),
            ("desktop-16-10", 1920, 1200, 1920, 1200),
            ("ultrawide-21-9", 3440, 1440, 3440, 1440),
            ("square-1-1", 1024, 1024, 1024, 1024),
            ("high-density-4k", 3840, 2160, 3840, 2160),
            ("high-density-5k", 5120, 2880, 5120, 2880),
        ]
        display_classes = accessibility.get("displayClasses")
        if not isinstance(display_classes, list) or len(display_classes) != 8:
            errors.append(f"{accessibility_label}.displayClasses must contain eight rows")
        elif not all(isinstance(row, dict) for row in display_classes):
            errors.append(f"{accessibility_label}.displayClasses must contain objects")
        else:
            for index, expected_display in enumerate(expected_display_rows):
                row = display_classes[index]
                row_label = f"{accessibility_label}.displayClasses[{index}]"
                actual_display = (
                    row.get("id"),
                    row.get("requestedWidth"),
                    row.get("requestedHeight"),
                    row.get("effectiveWidth"),
                    row.get("effectiveHeight"),
                )
                if actual_display != expected_display:
                    errors.append(f"{row_label} display shape drifted")
                _expect(row, "textScale", 1.5, row_label, errors)
                _expect(row, "logicalLayoutComplete", True, row_label, errors)
                viewport_scale = row.get("viewportScale")
                if (
                    not isinstance(viewport_scale, (int, float))
                    or isinstance(viewport_scale, bool)
                    or viewport_scale <= 0
                ):
                    errors.append(f"{row_label}.viewportScale must be positive")

        expected_accessibility_sources = [
            ("accessibility_presentation.json", "accessibility-presentation-v1"),
            ("shell_presentation.json", "shell-presentation-v1"),
            ("settings_screen.json", "settings-screen-qualification-v1"),
            ("input_cadence.json", "input-cadence-qualification-v1"),
            ("audio_fallback_stress.json", "audio-mixing-policy-v2"),
            ("multimodal_feedback.json", "multimodal-feedback-v1"),
            ("viewport_matrix.json", "virtual-viewport-matrix-v1"),
        ]
        accessibility_sources = accessibility.get("sources")
        if not isinstance(accessibility_sources, list) or len(accessibility_sources) != 7:
            errors.append(f"{accessibility_label}.sources must contain seven rows")
        elif not all(isinstance(source, dict) for source in accessibility_sources):
            errors.append(f"{accessibility_label}.sources must contain objects")
        else:
            actual_sources = [(source.get("fileName"), source.get("kind")) for source in accessibility_sources]
            if actual_sources != expected_accessibility_sources:
                errors.append(f"{accessibility_label}.sources must use the exact evidence order")
            for index, source in enumerate(accessibility_sources):
                source_label = f"{accessibility_label}.sources[{index}]"
                file_name = source.get("fileName")
                source_sha = source.get("sha256")
                if not SHA256_PATTERN.fullmatch(str(source_sha)):
                    errors.append(f"{source_label}.sha256 must be a SHA-256 digest")
                    continue
                if not isinstance(file_name, str) or Path(file_name).name != file_name:
                    errors.append(f"{source_label}.fileName must be a local base name")
                    continue
                source_path = evidence_root / file_name
                if not source_path.is_file():
                    errors.append(f"missing {source_label} bound evidence: {source_path}")
                elif _sha256(source_path) != source_sha:
                    errors.append(f"{source_label}.sha256 does not match {file_name}")

        _expect(
            accessibility,
            "pendingHumanChecks",
            [
                "retained-visible-audit-windows-macos-linux",
                "maximum-text-scale-platform-captures",
                "physical-keyboard-and-controller-only-flow-review",
                "players-using-relevant-accessibility-settings",
                "human-focus-contrast-readability-photosensitivity-review",
            ],
            accessibility_label,
            errors,
        )

        if expected_build_mode == "Release":
            _expect(launches, "schemaVersion", 1, f"{platform} launches", errors)
            _expect(
                launches,
                "kind",
                "candidate-launch-reliability-v1",
                f"{platform} launches",
                errors,
            )
            _expect(launches, "passed", True, f"{platform} launches", errors)
            _expect(launches, "platformId", platform, f"{platform} launches", errors)
            _expect(launches, "buildMode", "Release", f"{platform} launches", errors)
            _expect(
                launches,
                "sourceRevision",
                expected_revision,
                f"{platform} launches",
                errors,
            )
            _expect(launches, "requestedLaunches", 100, f"{platform} launches", errors)
            _expect(launches, "completedLaunches", 100, f"{platform} launches", errors)
            _expect(launches, "freshProfileLaunches", 100, f"{platform} launches", errors)
            _expect(launches, "readOnlyInstall", True, f"{platform} launches", errors)
            _expect(launches, "headless", True, f"{platform} launches", errors)
            _expect(launches, "failures", [], f"{platform} launches", errors)

            _expect(lifecycle, "schemaVersion", 1, f"{platform} lifecycle", errors)
            _expect(
                lifecycle,
                "kind",
                "candidate-install-lifecycle-preflight-v1",
                f"{platform} lifecycle",
                errors,
            )
            _expect(lifecycle, "platformId", platform, f"{platform} lifecycle", errors)
            _expect(lifecycle, "buildMode", "Release", f"{platform} lifecycle", errors)
            _expect(
                lifecycle,
                "sourceRevision",
                expected_revision,
                f"{platform} lifecycle",
                errors,
            )
            _expect_true_fields(
                lifecycle,
                (
                    "passed",
                    "firstInstallPassed",
                    "readOnlyInstallPassed",
                    "noElevationRequested",
                    "nonAsciiInstallAndUserPathsPassed",
                    "repairSnapshotMatched",
                    "repairLaunchPassed",
                    "futureSchemaRejectedAndPreserved",
                    "rollbackNeverOverwritesNewerPreferences",
                    "optionalPackAddRemovalRestorePassed",
                    "dataResetBackupRestorePassed",
                    "applicationRemovalPreservedPlayerData",
                    "completeSupportedSaveFixtureMatrix",
                ),
                f"{platform} lifecycle",
                errors,
            )
            _expect(
                lifecycle,
                "preferenceMigrationFixtureCount",
                6,
                f"{platform} lifecycle",
                errors,
            )
            preference_migrations = lifecycle.get("preferenceMigrations")
            expected_preference_migrations = [
                {
                    "inputSchema": schema,
                    "effectiveSchema": 7,
                    "loadCode": "Success",
                    "sourcePreserved": True,
                }
                for schema in range(1, 7)
            ]
            if preference_migrations != expected_preference_migrations:
                errors.append(f"{platform} lifecycle.preferenceMigrations must cover schemas 1 through 6")
            _expect(
                lifecycle,
                "additionalSaveMigrationFixtureCount",
                2,
                f"{platform} lifecycle",
                errors,
            )
            expected_additional_migrations = [
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
            ]
            if lifecycle.get("additionalSaveMigrations") != expected_additional_migrations:
                errors.append(f"{platform} lifecycle.additionalSaveMigrations must cover both schema-1 stores")
            _expect(
                lifecycle,
                "supportedSaveMigrationFixtureCount",
                8,
                f"{platform} lifecycle",
                errors,
            )
            _expect(
                lifecycle,
                "remainingGates",
                [
                    "selected-channel-installer-lifecycle",
                    "cross-version-binary-rollback",
                ],
                f"{platform} lifecycle",
                errors,
            )

        rows.append(
            {
                "platform": platform,
                "artifactManifestSha256": manifest_sha,
                "packageSha256": package_sha,
                "fileCount": manifest.get("fileCount"),
                "totalBytes": manifest.get("totalBytes"),
                "runtimeIdentifier": dependencies.get("runtimeIdentifier"),
                "signingState": signing.get("signingState"),
                "cleanLaunches": launches.get("completedLaunches") if isinstance(launches, dict) else 0,
                "installLifecyclePreflight": lifecycle.get("passed") if isinstance(lifecycle, dict) else False,
                "supportedSaveMigrationFixtures": lifecycle.get("supportedSaveMigrationFixtureCount", 0)
                if isinstance(lifecycle, dict)
                else 0,
                "reliabilityComparedSteps": reliability.get("totalComparedSimulationSteps", 0),
                "spectatorRestarts": spectator_restarts.get("completedRestarts", 0)
                if isinstance(spectator_restarts, dict)
                else 0,
                "completedFaults": faults.get("completedFaultCount", 0),
                "crashTriageRetained": isinstance(faults.get("crashTriage"), dict)
                and faults["crashTriage"].get("reportRetained") is True,
                "divergenceTriageRetained": isinstance(faults.get("divergenceTriage"), dict)
                and faults["divergenceTriage"].get("reportRetained") is True,
                "performanceSamples": performance_sample_count,
                "maximumPerformanceP99Milliseconds": maximum_p99,
                "accessibilityAuditPassed": accessibility.get("passed") is True,
                "maximumTextScaleDisplayClasses": accessibility.get("maximumTextScaleDisplayClassCount", 0),
            }
        )

    if len(rows) != len(PLATFORMS):
        errors.append(f"release matrix must contain exactly {len(PLATFORMS)} complete platform rows")
    if len(smoke_hashes) != 1:
        errors.append("all platform artifacts must report one identical smoke state hash")
    if len(lock_hashes) != 1:
        errors.append("all platform dependency inventories must report one lock-set SHA-256")
    if len(product_versions) != 1:
        errors.append("all platform output plans must report one product version")
    for mode_id, trace_hashes in reliability_trace_hashes.items():
        if len(trace_hashes) != 1:
            errors.append(f"all platform reliability rows must report one {mode_id} trace SHA-256")
    if len(performance_rules_hashes) != 1:
        errors.append("all platform performance rows must report one rules state hash")

    evidence = {
        "schemaVersion": 1,
        "kind": "release-matrix-qualification-v1",
        "passed": not errors,
        "sourceRevision": expected_revision,
        "buildMode": expected_build_mode,
        "platforms": rows,
        "sharedSmokeStateHash": next(iter(smoke_hashes)) if len(smoke_hashes) == 1 else None,
        "sharedLockSetSha256": next(iter(lock_hashes)) if len(lock_hashes) == 1 else None,
        "productVersion": next(iter(product_versions)) if len(product_versions) == 1 else None,
        "sharedReliabilityTraceSha256ByMode": {
            mode_id: next(iter(trace_hashes)) if len(trace_hashes) == 1 else None
            for mode_id, trace_hashes in reliability_trace_hashes.items()
        },
        "sharedPerformanceRulesStateHash": next(iter(performance_rules_hashes))
        if len(performance_rules_hashes) == 1
        else None,
        "totalCleanLaunches": sum(row["cleanLaunches"] for row in rows),
        "installLifecyclePreflightPlatforms": sum(1 for row in rows if row["installLifecyclePreflight"]),
        "totalSupportedSaveMigrationFixtures": sum(row["supportedSaveMigrationFixtures"] for row in rows),
        "totalReliabilityComparedSteps": sum(row["reliabilityComparedSteps"] for row in rows),
        "totalSpectatorRestarts": sum(row["spectatorRestarts"] for row in rows),
        "totalInjectedFaults": sum(row["completedFaults"] for row in rows),
        "crashTriagePlatforms": sum(1 for row in rows if row["crashTriageRetained"]),
        "divergenceTriagePlatforms": sum(1 for row in rows if row["divergenceTriageRetained"]),
        "totalPerformanceSamples": sum(row["performanceSamples"] for row in rows),
        "maximumSharedHostP99Milliseconds": max(
            (row["maximumPerformanceP99Milliseconds"] for row in rows), default=0.0
        ),
        "accessibilityAuditPlatforms": sum(1 for row in rows if row["accessibilityAuditPassed"]),
        "totalMaximumTextScaleDisplayClasses": sum(row["maximumTextScaleDisplayClasses"] for row in rows),
        "publicationEligible": False,
        "remainingProtectedOperations": [
            "windows-signing-verification",
            "macos-signing-notarization-stapling-verification",
            "linux-runtime-baseline-and-desktop-integration",
            "selected-channel-installer-lifecycle",
            "cross-version-binary-rollback",
            "named-minimum-hardware-performance-acceptance",
            "retained-accessibility-audit-and-user-review",
            "final-provenance",
            "channel-approval",
        ],
        "errors": errors,
    }
    return errors, evidence


def main(argv: list[str] | None = None) -> int:
    """Validate downloaded matrix artifacts and retain one aggregate record."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("download_root", type=Path)
    parser.add_argument("--expected-revision", required=True)
    parser.add_argument("--expected-build-mode", choices=("Debug", "Release"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)

    errors, evidence = validate_release_matrix(
        args.download_root.resolve(), args.expected_revision, args.expected_build_mode
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    if errors:
        print("Release matrix qualification failed:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1
    print(
        f"Release matrix qualification passed for {len(evidence['platforms'])} platforms at {args.expected_revision}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
