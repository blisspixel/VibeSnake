"""Contracts for hash-bound human radio listening records."""

from __future__ import annotations

import hashlib
import json
import runpy
from pathlib import Path

import pytest


MODULE = runpy.run_path(str(Path(__file__).resolve().parents[2] / "scripts" / "manual" / "review_radio_copies.py"))
CONFIRMATION_FIELDS = MODULE["CONFIRMATION_FIELDS"]
CRITERIA = MODULE["CRITERIA"]
RadioListeningReviewError = MODULE["RadioListeningReviewError"]
prepare_template = MODULE["prepare_template"]
validate_listening_record = MODULE["validate_listening_record"]
verify_review_handoff = MODULE["verify_review_handoff"]


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _write_review_set(path: Path) -> None:
    path.mkdir(parents=True)
    rows = []
    for index, content in enumerate((b"lossless-review-one", b"lossless-review-two"), start=1):
        output_name = f"the_bureau_track_{index}.review.flac"
        (path / output_name).write_bytes(content)
        rows.append(
            {
                "assetId": f"asset:audio/radio/the_bureau_track_{index}.mp3",
                "outputFile": output_name,
                "outputSha256": _sha256(content),
                "outputBytes": len(content),
                "stationId": "the_bureau",
                "technicalPass": True,
                "failures": [],
            }
        )
    manifest = {
        "schemaVersion": 1,
        "kind": "vibesnake-radio-review-copy-set-v1",
        "stationId": "the_bureau",
        "technicalPass": True,
        "releaseApproved": False,
        "sourceReplacementApproved": False,
        "exportEligibilityChanged": False,
        "humanListeningRequired": True,
        "humanListeningStatus": "pending",
        "sourceBytesModified": False,
        "modifiedSourcePaths": [],
        "summary": {
            "trackCount": 2,
            "technicalPassCount": 2,
            "technicalFailureCount": 0,
        },
        "reviewCopies": rows,
    }
    (path / "review-copy-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def _completed_record(template: dict[str, object]) -> dict[str, object]:
    template["reviewerId"] = "radio-reviewer-001"
    template["executedUtc"] = "2026-08-20T12:00:00Z"
    for review in template["trackReviews"]:
        review["reviewedDeviceIds"] = ["headphones", "speakers"]
        for criterion in review["criteria"]:
            criterion["result"] = "pass"
        review["decision"] = "approve-source-replacement"
    template["confirmations"] = {field: True for field in CONFIRMATION_FIELDS}
    return template


def test_template_binds_every_copy_and_remains_explicitly_pending(tmp_path: Path) -> None:
    review_directory = tmp_path / "the_bureau"
    _write_review_set(review_directory)
    output = review_directory / "listening-review.json.template"

    template = prepare_template(review_directory, output)

    assert output.is_file()
    assert template["stationId"] == "the_bureau"
    assert len(template["trackReviews"]) == 2
    assert {review["decision"] for review in template["trackReviews"]} == {"pending"}
    assert all(review["reviewedDeviceIds"] == [] for review in template["trackReviews"])
    assert all(
        [criterion["criterionId"] for criterion in review["criteria"]] == list(CRITERIA)
        for review in template["trackReviews"]
    )
    assert template["confirmations"] == {field: False for field in CONFIRMATION_FIELDS}

    handoff = verify_review_handoff(review_directory)
    assert handoff["passed"] is True
    assert handoff["technicalInputsVerified"] is True
    assert handoff["trackCount"] == 2
    assert handoff["humanListeningStatus"] == "pending"
    assert handoff["sourceReplacementApproved"] is False

    with pytest.raises(RadioListeningReviewError, match="overwrite"):
        prepare_template(review_directory, output)


def test_complete_approval_requires_all_tracks_criteria_and_devices(tmp_path: Path) -> None:
    review_directory = tmp_path / "the_bureau"
    _write_review_set(review_directory)
    template_path = review_directory / "listening-review.json.template"
    record = _completed_record(prepare_template(review_directory, template_path))
    record_path = review_directory / "listening-review.json"
    record_path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")

    errors, evidence = validate_listening_record(review_directory, record_path)

    assert errors == []
    assert evidence["passed"] is True
    assert evidence["technicalInputsVerified"] is True
    assert evidence["listeningComplete"] is True
    assert evidence["approvedTrackCount"] == 2
    assert evidence["sourceReplacementApproved"] is True
    assert evidence["releaseApproved"] is False
    assert evidence["exportEligibilityChanged"] is False


def test_honest_rejection_is_complete_but_does_not_approve_sources(tmp_path: Path) -> None:
    review_directory = tmp_path / "the_bureau"
    _write_review_set(review_directory)
    template_path = review_directory / "listening-review.json.template"
    record = _completed_record(prepare_template(review_directory, template_path))
    first = record["trackReviews"][0]
    first["criteria"][4]["result"] = "fail"
    first["decision"] = "reject-source-replacement"
    first["findingIds"] = ["radio-finding-001"]
    record_path = review_directory / "listening-review.json"
    record_path.write_text(json.dumps(record), encoding="utf-8")

    errors, evidence = validate_listening_record(review_directory, record_path)

    assert errors == []
    assert evidence["listeningComplete"] is True
    assert evidence["approvedTrackCount"] == 1
    assert evidence["rejectedTrackCount"] == 1
    assert evidence["sourceReplacementApproved"] is False


def test_record_rejects_inconsistent_approval_or_tampered_copy(tmp_path: Path) -> None:
    review_directory = tmp_path / "the_bureau"
    _write_review_set(review_directory)
    template_path = review_directory / "listening-review.json.template"
    record = _completed_record(prepare_template(review_directory, template_path))
    record["trackReviews"][0]["criteria"][0]["result"] = "fail"
    record_path = review_directory / "listening-review.json"
    record_path.write_text(json.dumps(record), encoding="utf-8")

    errors, evidence = validate_listening_record(review_directory, record_path)

    assert any("cannot approve unless every criterion passes" in error for error in errors)
    assert evidence["listeningComplete"] is False
    assert evidence["sourceReplacementApproved"] is False

    (review_directory / "the_bureau_track_1.review.flac").write_bytes(b"tampered")
    errors, evidence = validate_listening_record(review_directory, record_path)

    assert any("size mismatch" in error or "SHA-256 mismatch" in error for error in errors)
    assert evidence["technicalInputsVerified"] is False
    assert evidence["sourceReplacementApproved"] is False


def test_malformed_findings_and_invalid_calendar_time_fail_closed(tmp_path: Path) -> None:
    review_directory = tmp_path / "the_bureau"
    _write_review_set(review_directory)
    template_path = review_directory / "listening-review.json.template"
    record = _completed_record(prepare_template(review_directory, template_path))
    record["executedUtc"] = "2026-99-99T99:99:99Z"
    record["trackReviews"][0]["findingIds"] = [{"unexpected": True}]
    record_path = review_directory / "listening-review.json"
    record_path.write_text(json.dumps(record), encoding="utf-8")

    errors, evidence = validate_listening_record(review_directory, record_path)

    assert any("executedUtc must use" in error for error in errors)
    assert any("findingIds must contain" in error for error in errors)
    assert evidence["listeningComplete"] is False
    assert evidence["sourceReplacementApproved"] is False


def test_listening_record_must_remain_with_its_exact_review_set(tmp_path: Path) -> None:
    review_directory = tmp_path / "the_bureau"
    _write_review_set(review_directory)
    template_path = review_directory / "listening-review.json.template"
    record = _completed_record(prepare_template(review_directory, template_path))
    record_path = tmp_path / "detached-listening-review.json"
    record_path.write_text(json.dumps(record), encoding="utf-8")

    errors, evidence = validate_listening_record(review_directory, record_path)

    assert errors == ["radio listening record must be directly inside its review directory"]
    assert evidence["technicalInputsVerified"] is False
    assert evidence["sourceReplacementApproved"] is False
