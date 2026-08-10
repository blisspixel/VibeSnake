"""Contracts for the release-candidate freeze policy."""

from __future__ import annotations

import copy
import json
from pathlib import Path

import pytest

from scripts.check_candidate_freeze import (
    POLICY_PATH,
    ROOT,
    _glob_files,
    _validate_policy_shape,
    build_manifest,
    validate_policy,
)


def test_repository_candidate_freeze_policy_is_valid_and_inactive() -> None:
    policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))

    errors, file_count = validate_policy(ROOT, POLICY_PATH)

    assert errors == []
    assert file_count >= 100
    assert policy["state"] == "pre-freeze"
    assert all(value is None for value in policy["activation"].values())
    assert {gate["state"] for gate in policy["prerequisiteGates"]} == {"open"}


def test_policy_rejects_unapproved_change_kind() -> None:
    policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    policy["allowedChangeKinds"].append("feature")

    errors, _ = _validate_policy_shape(ROOT, policy)

    assert "allowedChangeKinds must be " in "\n".join(errors)


def test_policy_rejects_activation_while_a_prerequisite_is_open() -> None:
    policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    policy["state"] = "frozen"
    policy["activation"] = {
        "candidateRevision": "a" * 40,
        "activatedUtc": "2026-08-09T12:00:00Z",
        "baselineManifest": "config/candidate_freeze_baseline_v1.json",
        "baselineSha256": "b" * 64,
    }

    errors, _ = _validate_policy_shape(ROOT, policy)

    assert "every prerequisite gate must pass before the policy is frozen" in errors


def test_baseline_manifest_is_deterministic_and_hashes_every_surface() -> None:
    policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    errors, surfaces = _validate_policy_shape(ROOT, policy)
    assert errors == []

    first = build_manifest(ROOT, policy, surfaces, "a" * 40, "2026-08-09T12:00:00Z")
    second = build_manifest(ROOT, policy, surfaces, "a" * 40, "2026-08-09T12:00:00Z")

    assert first == second
    assert len(first["files"]) == len(surfaces)
    assert len(first["combinedSha256"]) == 64
    assert first["files"] == sorted(first["files"], key=lambda entry: entry["path"])


def test_baseline_builder_rejects_ambiguous_identity() -> None:
    policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    errors, surfaces = _validate_policy_shape(ROOT, policy)
    assert errors == []

    with pytest.raises(ValueError, match="40-character"):
        build_manifest(ROOT, policy, surfaces, "main", "2026-08-09T12:00:00Z")
    with pytest.raises(ValueError, match="YYYY-MM-DD"):
        build_manifest(ROOT, policy, surfaces, "a" * 40, "today")


def test_policy_rejects_unsafe_or_empty_surface_patterns(tmp_path: Path) -> None:
    policy = copy.deepcopy(json.loads(POLICY_PATH.read_text(encoding="utf-8")))
    policy["frozenContracts"][0]["pathPatterns"] = ["../outside/**/*.cs"]

    errors, surfaces = _validate_policy_shape(tmp_path, policy)

    assert surfaces == {}
    assert any("unsafe path pattern" in error for error in errors)
    assert any("matched no files" in error for error in errors)


def test_globstar_includes_direct_and_nested_files(tmp_path: Path) -> None:
    surface = tmp_path / "native" / "src" / "VibeSnake.Rules"
    direct = surface / "Direct.cs"
    nested = surface / "Nested" / "Nested.cs"
    nested.parent.mkdir(parents=True)
    direct.write_text("direct", encoding="utf-8")
    nested.write_text("nested", encoding="utf-8")

    matches = _glob_files(tmp_path, "native/src/VibeSnake.Rules/**/*.cs")

    assert matches == (direct, nested)
