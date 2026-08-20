from __future__ import annotations

import runpy
from pathlib import Path

import pytest


MODULE = runpy.run_path(
    str(Path(__file__).resolve().parents[2] / "scripts" / "manual" / "prepare_radio_review_copies.py")
)
RadioReviewCopyError = MODULE["RadioReviewCopyError"]
build_second_pass_filter = MODULE["build_second_pass_filter"]
build_trim_filter = MODULE["build_trim_filter"]
compute_trim_plan = MODULE["compute_trim_plan"]
compute_edge_correction = MODULE["compute_edge_correction"]
parse_loudnorm_output = MODULE["parse_loudnorm_output"]
validate_review_measurement = MODULE["validate_review_measurement"]
validate_replace_target = MODULE["validate_replace_target"]


def test_parse_loudnorm_output_requires_one_complete_finite_summary() -> None:
    output = """
    [Parsed_loudnorm_0] {
        "input_i" : "-13.20",
        "input_tp" : "0.30",
        "input_lra" : "4.10",
        "input_thresh" : "-23.60",
        "output_i" : "-18.00",
        "output_tp" : "-1.00",
        "output_lra" : "4.00",
        "output_thresh" : "-28.40",
        "normalization_type" : "dynamic",
        "target_offset" : "0.00"
    }
    """

    assert parse_loudnorm_output(output) == {
        "inputIntegratedLufs": -13.2,
        "inputTruePeakDbtp": 0.3,
        "inputLoudnessRangeLu": 4.1,
        "inputThresholdLufs": -23.6,
        "outputIntegratedLufs": -18.0,
        "outputTruePeakDbtp": -1.0,
        "outputLoudnessRangeLu": 4.0,
        "outputThresholdLufs": -28.4,
        "targetOffsetDb": 0.0,
        "normalizationType": "dynamic",
    }


def test_parse_loudnorm_output_rejects_nonfinite_or_duplicate_summaries() -> None:
    invalid = """{
        "input_i":"-inf", "input_tp":"0", "input_lra":"1", "input_thresh":"-20",
        "output_i":"-18", "output_tp":"-1", "output_lra":"1", "output_thresh":"-28",
        "target_offset":"0", "normalization_type":"linear"
    }"""
    with pytest.raises(RadioReviewCopyError, match="not finite"):
        parse_loudnorm_output(invalid)


def test_trim_plan_keeps_quarter_second_edges_without_touching_internal_silence() -> None:
    plan = compute_trim_plan(
        {
            "durationSeconds": 270.03,
            "leadingSilenceSeconds": 7.82,
            "trailingSilenceSeconds": 3.64,
        }
    )

    assert plan == {
        "sourceDurationSeconds": 270.03,
        "startSeconds": 7.57,
        "endSeconds": 266.64,
        "expectedOutputDurationSeconds": 259.07,
        "retainedEdgeSilenceSeconds": 0.25,
    }
    assert build_trim_filter(plan) == "atrim=start=7.57:end=266.64,asetpts=PTS-STARTPTS"


def test_trim_plan_rejects_overlapping_or_undersized_audio() -> None:
    with pytest.raises(RadioReviewCopyError, match="invalid edge-silence"):
        compute_trim_plan({"durationSeconds": 30.0, "leadingSilenceSeconds": 15.0, "trailingSilenceSeconds": 15.0})
    with pytest.raises(RadioReviewCopyError, match="undersized"):
        compute_trim_plan({"durationSeconds": 35.0, "leadingSilenceSeconds": 3.0, "trailingSilenceSeconds": 3.0})


def test_second_pass_filter_binds_every_first_pass_measurement() -> None:
    result = build_second_pass_filter(
        "atrim=start=0:end=100,asetpts=PTS-STARTPTS",
        {
            "inputIntegratedLufs": -13.2,
            "inputTruePeakDbtp": 0.3,
            "inputLoudnessRangeLu": 4.1,
            "inputThresholdLufs": -23.6,
            "targetOffsetDb": 0.0,
        },
        44100,
    )
    assert result == (
        "atrim=start=0:end=100,asetpts=PTS-STARTPTS,"
        "loudnorm=I=-18:TP=-1:LRA=50:measured_I=-13.2:measured_LRA=4.1:measured_TP=0.3:"
        "measured_thresh=-23.6:offset=0:linear=true:print_format=json,aresample=44100"
    )


def test_edge_correction_only_removes_the_measured_policy_excess_plus_margin() -> None:
    assert compute_edge_correction({"leadingSilenceSeconds": 2.061111, "trailingSilenceSeconds": 1.5}) == {
        "additionalStartSeconds": 0.161111,
        "additionalEndSeconds": 0.0,
    }


def test_review_measurement_accepts_a_policy_conforming_lossless_copy() -> None:
    assert (
        validate_review_measurement(
            {"codec": "flac", "channels": 2, "sampleRateHz": 44100, "durationSeconds": 100.01},
            {"integratedLufs": -18.0, "truePeakDbtp": -1.0},
            {
                "leadingSilenceSeconds": 0.25,
                "trailingSilenceSeconds": 0.25,
                "maximumInternalSilenceSeconds": 0.0,
            },
            2,
            44100,
            100.0,
        )
        == []
    )


def test_review_measurement_reports_each_independent_policy_failure() -> None:
    failures = validate_review_measurement(
        {"codec": "mp3", "channels": 1, "sampleRateHz": 48000, "durationSeconds": 90.0},
        {"integratedLufs": -10.0, "truePeakDbtp": 0.0},
        {
            "leadingSilenceSeconds": 3.0,
            "trailingSilenceSeconds": 3.0,
            "maximumInternalSilenceSeconds": 6.0,
        },
        2,
        44100,
        100.0,
    )

    assert len(failures) == 9


def test_replace_target_requires_a_matching_tool_owned_manifest(tmp_path: Path) -> None:
    target = tmp_path / "station"
    target.mkdir()
    with pytest.raises(RadioReviewCopyError, match="without its manifest"):
        validate_replace_target(target, "station")

    (target / "review-copy-manifest.json").write_text(
        '{"kind":"foreign","schemaVersion":1,"stationId":"station"}',
        encoding="utf-8",
    )
    with pytest.raises(RadioReviewCopyError, match="foreign manifest"):
        validate_replace_target(target, "station")

    (target / "review-copy-manifest.json").write_text(
        '{"kind":"vibesnake-radio-review-copy-set-v1","schemaVersion":1,"stationId":"station"}',
        encoding="utf-8",
    )
    validate_replace_target(target, "station")
