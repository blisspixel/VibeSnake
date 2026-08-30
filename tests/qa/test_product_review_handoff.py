"""Contracts for exact-candidate manual product review preparation."""

from __future__ import annotations

import json
import runpy
from pathlib import Path

import pytest


MODULE = runpy.run_path(str(Path(__file__).resolve().parents[2] / "scripts" / "manual" / "prepare_product_review.py"))
ProductReviewPreparationError = MODULE["ProductReviewPreparationError"]
build_candidate = MODULE["build_candidate"]
build_session_template = MODULE["build_session_template"]
prepare_workspace = MODULE["prepare_workspace"]

REVISION = "a" * 40


def _matrix() -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "kind": "release-matrix-qualification-v1",
        "passed": True,
        "sourceRevision": REVISION,
        "buildMode": "Release",
        "productVersion": "0.3.0-alpha.1",
        "platforms": [
            {
                "platform": "windows-x64",
                "artifactManifestSha256": "1" * 64,
                "packageSha256": "2" * 64,
                "packageBytes": 100,
                "directDownloadFileName": "VibeSnake-windows.zip",
            },
            {
                "platform": "macos-universal",
                "artifactManifestSha256": "3" * 64,
                "packageSha256": "4" * 64,
                "packageBytes": 200,
                "directDownloadFileName": "VibeSnake-macos.zip",
            },
            {
                "platform": "linux-x64",
                "artifactManifestSha256": "5" * 64,
                "packageSha256": "6" * 64,
                "packageBytes": 300,
                "directDownloadFileName": "VibeSnake-linux.tar.gz",
            },
        ],
    }


def test_candidate_projects_one_verified_matrix_into_four_physical_rows() -> None:
    candidate = build_candidate(_matrix(), "f" * 64, 123456, "blisspixel/VibeSnake")

    assert candidate["candidateRevision"] == REVISION
    assert candidate["releaseRunId"] == 123456
    assert candidate["humanReviewStatus"] == "pending"
    assert candidate["releaseAcceptance"] is False
    assert candidate["publicationEligible"] is False
    assert [row["platformRowId"] for row in candidate["artifactRows"]] == [
        "windows-x64",
        "macos-universal-apple-silicon",
        "macos-universal-intel",
        "linux-x64",
    ]
    assert candidate["artifactRows"][1]["sha256"] == candidate["artifactRows"][2]["sha256"] == "4" * 64


def test_session_template_binds_identity_and_cannot_accidentally_pass() -> None:
    candidate = build_candidate(_matrix(), "f" * 64, 123456, "blisspixel/VibeSnake")
    template = build_session_template(candidate, candidate["artifactRows"][0])

    assert template["candidateRevision"] == REVISION
    assert template["artifactSha256"] == "2" * 64
    assert template["appVersion"] == "0.3.0-alpha.1"
    assert template["schemaVersion"] == 2
    assert template["kind"] == "vibesnake-manual-product-matrix-session-v2"
    assert len(template["results"]) == 36
    assert {row["result"] for row in template["results"]} == {"pending"}
    assert {row["inputDeviceId"] for row in template["results"]} == {"REPLACE"}
    assert all(row["inputCapabilityIds"] == [] for row in template["results"])
    assert all(row["settingsProfileIds"] == [] for row in template["results"])


def test_workspace_recomputes_matrix_and_is_deterministic_and_fail_closed(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    release_root = tmp_path / "release"
    matrix_path = release_root / "vibesnake-release-matrix" / "release_matrix.json"
    matrix_path.parent.mkdir(parents=True)
    matrix = _matrix()
    matrix_path.write_text(json.dumps(matrix, indent=2) + "\n", encoding="utf-8")
    monkeypatch.setitem(prepare_workspace.__globals__, "validate_release_matrix", lambda *_: ([], matrix))

    output, manifest = prepare_workspace(
        release_root,
        REVISION,
        123456,
        "blisspixel/VibeSnake",
        tmp_path / "output",
    )

    assert output == (tmp_path / "output" / REVISION).resolve()
    assert manifest["humanReviewStatus"] == "pending"
    assert manifest["releaseAcceptance"] is False
    assert len(manifest["files"]) == 6
    candidate = json.loads((output / "candidate.json").read_text(encoding="utf-8"))
    assert candidate["candidateRevision"] == REVISION
    assert len(list((output / "templates").glob("*.json.template"))) == 4
    assert list((output / "sessions").glob("*.json")) == []

    with pytest.raises(ProductReviewPreparationError, match="already exists"):
        prepare_workspace(
            release_root,
            REVISION,
            123456,
            "blisspixel/VibeSnake",
            tmp_path / "output",
        )


def test_workspace_refuses_unverified_or_mismatched_release_evidence(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    release_root = tmp_path / "release"
    matrix_path = release_root / "vibesnake-release-matrix" / "release_matrix.json"
    matrix_path.parent.mkdir(parents=True)
    matrix_path.write_text(json.dumps(_matrix()), encoding="utf-8")
    monkeypatch.setitem(
        prepare_workspace.__globals__, "validate_release_matrix", lambda *_: (["failed gate"], _matrix())
    )

    with pytest.raises(ProductReviewPreparationError, match="validation failed"):
        prepare_workspace(
            release_root,
            REVISION,
            123456,
            "blisspixel/VibeSnake",
            tmp_path / "output",
        )


def test_workspace_rejects_duplicate_fields_in_retained_matrix(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    release_root = tmp_path / "release"
    matrix_path = release_root / "vibesnake-release-matrix" / "release_matrix.json"
    matrix_path.parent.mkdir(parents=True)
    source = json.dumps(_matrix(), indent=2)
    source = source.replace('"schemaVersion": 1,', '"schemaVersion": 1,\n  "schemaVersion": 1,', 1)
    matrix_path.write_text(source, encoding="utf-8")
    monkeypatch.setitem(prepare_workspace.__globals__, "validate_release_matrix", lambda *_: ([], _matrix()))

    with pytest.raises(ProductReviewPreparationError, match="duplicate JSON field: schemaVersion"):
        prepare_workspace(
            release_root,
            REVISION,
            123456,
            "blisspixel/VibeSnake",
            tmp_path / "output",
        )
