"""Contracts for targeted Python-to-C# Phase Shift fixtures."""

import logging
from pathlib import Path

from vibesnake.qa.contracts import CURRENT_RULESET
from vibesnake.qa.shared_phase_shift_traces import (
    PHASE_RANDOMNESS_POLICY,
    build_phase_shift_fixture,
    check_fixture,
    main,
)


FIXTURE_PATH = Path(__file__).parents[1] / "fixtures" / "shared" / "phase_shift_rules_v1.json"


def test_checked_in_phase_shift_fixture_matches_production():
    fixture = build_phase_shift_fixture()

    assert check_fixture(FIXTURE_PATH, fixture)
    assert fixture["ruleset"] == CURRENT_RULESET.to_dict()
    assert fixture["randomness_policy"] == PHASE_RANDOMNESS_POLICY
    assert fixture["schema_version"] == 1
    assert fixture["contract"] == "phase-shift-rules-targeted-v1"
    assert fixture["case_count"] == 6
    assert {case["id"] for case in fixture["cases"]} == {
        "phase-shift-collect-on-entry",
        "phase-shift-pickup-expiry",
        "phase-shift-active-countdown",
        "phase-shift-active-expiry-before-collision",
        "phase-shift-body-overlap",
        "phase-shift-does-not-block-starvation",
    }

    cases = {case["id"]: case for case in fixture["cases"]}
    assert cases["phase-shift-collect-on-entry"]["expected"]["phase_shift_ticks_remaining"] == 100
    assert cases["phase-shift-pickup-expiry"]["expected"]["pickup"] is None
    assert cases["phase-shift-active-countdown"]["expected"]["phase_shift_ticks_remaining"] == 1
    expiry = cases["phase-shift-active-expiry-before-collision"]["expected"]
    assert expiry["death_cause"] == "self_collision"
    assert expiry["phase_shift_ticks_remaining"] == 0
    overlap = cases["phase-shift-body-overlap"]["expected"]
    assert overlap["alive"] is True
    assert overlap["body"] == [[1, 2], [2, 2], [2, 1], [2, 2]]
    starvation = cases["phase-shift-does-not-block-starvation"]["expected"]
    assert starvation["death_cause"] == "starvation"
    assert starvation["phase_shift_ticks_remaining"] == 1


def test_phase_shift_fixture_cli_detects_stale_output(tmp_path):
    output = tmp_path / "phase.json"

    assert main(["--output", str(output)]) == 0
    assert main(["--output", str(output), "--check"]) == 0
    output.write_text("{}\n", encoding="utf-8")
    assert main(["--output", str(output), "--check"]) == 1


def test_phase_shift_fixture_cli_restores_package_logger_level(tmp_path):
    package_logger = logging.getLogger("vibesnake")
    previous_level = package_logger.level
    package_logger.setLevel(logging.DEBUG)
    try:
        assert main(["--output", str(tmp_path / "phase.json")]) == 0
        assert package_logger.level == logging.DEBUG
    finally:
        package_logger.setLevel(previous_level)
