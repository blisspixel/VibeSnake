"""Contracts for targeted Python-to-C# Shield fixtures."""

import logging
from pathlib import Path

from vibesnake.qa.contracts import CURRENT_RULESET
from vibesnake.qa.shared_power_traces import (
    POWER_RANDOMNESS_POLICY,
    build_power_fixture,
    check_fixture,
    main,
)


FIXTURE_PATH = Path(__file__).parents[1] / "fixtures" / "shared" / "shield_rules_v1.json"


def test_checked_in_power_fixture_matches_production_shield():
    fixture = build_power_fixture()

    assert check_fixture(FIXTURE_PATH, fixture)
    assert fixture["ruleset"] == CURRENT_RULESET.to_dict()
    assert fixture["randomness_policy"] == POWER_RANDOMNESS_POLICY
    assert fixture["schema_version"] == 1
    assert fixture["contract"] == "shield-rules-targeted-v1"
    assert fixture["case_count"] == 8
    assert {case["id"] for case in fixture["cases"]} == {
        "shield-collect-on-entry",
        "shield-pickup-expiry",
        "shield-active-countdown",
        "shield-active-expiry",
        "shield-collision-consumption",
        "shield-collision-at-starvation-deadline",
        "shield-expiry-before-collision",
        "shield-does-not-block-starvation",
    }

    cases = {case["id"]: case for case in fixture["cases"]}
    assert cases["shield-collect-on-entry"]["expected"]["shield_ticks_remaining"] == 100
    assert cases["shield-pickup-expiry"]["expected"]["pickup"] is None
    assert cases["shield-active-countdown"]["expected"]["shield_ticks_remaining"] == 1
    assert cases["shield-active-expiry"]["expected"]["shield_ticks_remaining"] == 0
    assert cases["shield-collision-consumption"]["expected"]["alive"] is True
    deadline = cases["shield-collision-at-starvation-deadline"]["expected"]
    assert deadline["death_cause"] == "starvation"
    assert deadline["shield_ticks_remaining"] == 0
    assert cases["shield-expiry-before-collision"]["expected"]["death_cause"] == "self_collision"
    starvation = cases["shield-does-not-block-starvation"]["expected"]
    assert starvation["death_cause"] == "starvation"
    assert starvation["shield_ticks_remaining"] == 1


def test_power_fixture_cli_detects_stale_output(tmp_path):
    output = tmp_path / "shield.json"

    assert main(["--output", str(output)]) == 0
    assert main(["--output", str(output), "--check"]) == 0
    output.write_text("{}\n", encoding="utf-8")
    assert main(["--output", str(output), "--check"]) == 1


def test_power_fixture_cli_restores_package_logger_level(tmp_path):
    package_logger = logging.getLogger("vibesnake")
    previous_level = package_logger.level
    package_logger.setLevel(logging.DEBUG)
    try:
        assert main(["--output", str(tmp_path / "shield.json")]) == 0
        assert package_logger.level == logging.DEBUG
    finally:
        package_logger.setLevel(previous_level)
