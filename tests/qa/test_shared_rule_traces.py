"""Contracts for targeted Python-to-C# rules fixtures."""

import logging
import random
from pathlib import Path

from vibesnake.qa.contracts import CURRENT_RULESET, SHARED_RANDOMNESS_POLICY
from vibesnake.qa.shared_rule_traces import build_rule_fixture, check_fixture, main


FIXTURE_PATH = Path(__file__).parents[1] / "fixtures" / "shared" / "core_rules_v4.json"


def test_checked_in_rule_fixture_matches_production_rules():
    fixture = build_rule_fixture()

    assert check_fixture(FIXTURE_PATH, fixture)
    assert fixture["ruleset"] == CURRENT_RULESET.to_dict()
    assert fixture["randomness_policy"] == SHARED_RANDOMNESS_POLICY
    assert fixture["schema_version"] == 4
    assert fixture["contract"] == "core-rules-targeted-v4"
    assert fixture["case_count"] == 35
    assert fixture["config"]["maximum_score"] == 2_000_000_000
    assert {case["id"] for case in fixture["cases"]} == {
        "food-entry",
        "food-buffered-turn",
        "queue-rejections-and-consumption",
        "queue-capacity",
        "combo-before-three",
        "combo-threshold-three",
        "combo-after-three",
        "combo-threshold-five",
        "combo-after-five",
        "combo-before-ten",
        "combo-threshold-ten",
        "combo-after-ten",
        "combo-before-twenty",
        "combo-threshold-twenty",
        "combo-after-twenty-cap",
        "speed-bonus-last-eligible-tick",
        "speed-bonus-exact-boundary",
        "speed-bonus-after-boundary",
        "combo-window-exact-no-food",
        "combo-window-expired-no-food",
        "combo-window-exact-food",
        "expired-combo-late-food-no-speed-bonus",
        "length-exact-ten",
        "length-first-bonus",
        "length-above-boundary",
        "score-saturation-near-cap",
        "score-at-cap",
        "self-collision",
        "departing-tail-is-safe",
        "horizontal-wrap",
        "starvation-predeadline",
        "starvation-deadline-food-rescue",
        "starvation-deadline-death",
        "starvation-collision-precedence",
        "full-grid-victory",
    }

    cases = {case["id"]: case for case in fixture["cases"]}
    assert cases["queue-rejections-and-consumption"]["command_acceptance"] == [
        False,
        False,
        True,
        False,
        True,
        False,
    ]
    assert cases["queue-capacity"]["command_acceptance"] == [True, True, True, False, False]
    assert cases["speed-bonus-last-eligible-tick"]["expected"]["events"][2]["value"] == 18
    assert cases["speed-bonus-exact-boundary"]["expected"]["events"][2]["value"] == 13
    assert cases["expired-combo-late-food-no-speed-bonus"]["expected"]["events"][2]["value"] == 13
    assert cases["score-saturation-near-cap"]["expected"]["events"][2]["value"] == 1
    assert cases["score-at-cap"]["expected"]["events"][2]["value"] == 0
    assert cases["score-at-cap"]["expected"]["score"] == 2_000_000_000
    assert cases["horizontal-wrap"]["initial"]["food"] == [5, 5]
    assert cases["horizontal-wrap"]["expected"]["food_unchanged"] is True
    assert cases["horizontal-wrap"]["expected"]["random_use"] == "unchanged"
    assert cases["starvation-deadline-food-rescue"]["expected"]["won"] is False
    assert cases["starvation-deadline-death"]["expected"]["events"][-1] == {
        "kind": "died",
        "position": [6, 5],
        "death_cause": "starvation",
    }
    assert cases["full-grid-victory"]["expected"]["won"] is True
    assert cases["full-grid-victory"]["expected"]["events"][-1]["kind"] == "won"
    assert cases["full-grid-victory"]["expected"]["random_respawn"] == "full_grid_no_cell"
    assert cases["full-grid-victory"]["expected"]["random_use"] == "unchanged"
    assert all(case["expected"]["food_unchanged"] for case in fixture["cases"] if not case["expected"]["ate_food"])
    assert {
        case["expected"]["random_respawn"]
        for case in fixture["cases"]
        if case["expected"]["ate_food"] and not case["expected"]["won"]
    } == {"legal_free_cell"}
    assert {
        case["expected"]["random_use"]
        for case in fixture["cases"]
        if case["expected"]["ate_food"] and not case["expected"]["won"]
    } == {"advanced"}


def test_rule_fixture_cli_detects_stale_output(tmp_path):
    output = tmp_path / "rules.json"

    assert main(["--output", str(output)]) == 0
    assert main(["--output", str(output), "--check"]) == 0
    output.write_text("{}\n", encoding="utf-8")
    assert main(["--output", str(output), "--check"]) == 1


def test_rule_fixture_generation_restores_process_random_state():
    random.seed(0xC01A)
    state_before = random.getstate()

    build_rule_fixture()

    assert random.getstate() == state_before


def test_rule_fixture_cli_restores_package_logger_level(tmp_path):
    package_logger = logging.getLogger("vibesnake")
    previous_level = package_logger.level
    package_logger.setLevel(logging.DEBUG)
    try:
        assert main(["--output", str(tmp_path / "rules.json")]) == 0
        assert package_logger.level == logging.DEBUG
    finally:
        package_logger.setLevel(previous_level)
