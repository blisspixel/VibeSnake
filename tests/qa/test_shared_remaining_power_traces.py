"""Contracts for targeted Python-to-C# remaining-power fixtures."""

from pathlib import Path

from vibesnake.qa.contracts import CURRENT_RULESET
from vibesnake.qa.shared_remaining_power_traces import (
    REMAINING_RANDOMNESS_POLICY,
    build_remaining_power_fixture,
    check_fixture,
    main,
)


FIXTURE_PATH = Path(__file__).parents[1] / "fixtures" / "shared" / "remaining_powers_rules_v1.json"


def test_checked_in_remaining_power_fixture_matches_production():
    fixture = build_remaining_power_fixture()

    assert check_fixture(FIXTURE_PATH, fixture)
    assert fixture["ruleset"] == CURRENT_RULESET.to_dict()
    assert fixture["randomness_policy"] == REMAINING_RANDOMNESS_POLICY
    assert fixture["contract"] == "remaining-powers-rules-targeted-v1"
    assert fixture["case_count"] == 9
    assert {case["id"] for case in fixture["cases"]} == {
        "slow-mo-collect-on-entry",
        "boost-collect-on-entry",
        "magnet-collect-on-entry",
        "magnet-pull-food-toward-head",
        "gluttony-collect-on-entry",
        "gluttony-eat-without-growth",
        "bait-collect-on-entry",
        "segment-detach-on-entry",
        "tempo-compose-active-countdown",
    }
    cases = {case["id"]: case for case in fixture["cases"]}
    magnet = cases["magnet-pull-food-toward-head"]["expected"]
    assert magnet["food"] == [5, 4]
    assert magnet["magnet_ticks_remaining"] == 2
    gluttony = cases["gluttony-eat-without-growth"]["expected"]
    assert len(gluttony["body"]) == 2
    assert gluttony["skip_food"] is True
    detach = cases["segment-detach-on-entry"]["expected"]
    assert detach["body"] == [[6, 1]]
    assert len(detach["detached_obstacles"]) == 5
    tempo = cases["tempo-compose-active-countdown"]["expected"]
    assert tempo["movement_cadence_numerator"] == 2
    assert tempo["movement_cadence_denominator"] == 2


def test_remaining_power_fixture_cli_detects_stale_output(tmp_path):
    output = tmp_path / "remaining_powers.json"
    assert main(["--output", str(output)]) == 0
    assert main(["--output", str(output), "--check"]) == 0
    output.write_text("{}\n", encoding="utf-8")
    assert main(["--output", str(output), "--check"]) == 1
