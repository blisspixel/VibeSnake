"""Contracts for targeted Python-to-C# Last Stand fixtures."""

from pathlib import Path

from vibesnake.qa.contracts import CURRENT_RULESET
from vibesnake.qa.shared_last_stand_traces import (
    LAST_STAND_RANDOMNESS_POLICY,
    build_last_stand_fixture,
    check_fixture,
    main,
)


FIXTURE_PATH = Path(__file__).parents[1] / "fixtures" / "shared" / "last_stand_rules_v1.json"


def test_checked_in_last_stand_fixture_matches_production():
    fixture = build_last_stand_fixture()

    assert check_fixture(FIXTURE_PATH, fixture)
    assert fixture["ruleset"] == CURRENT_RULESET.to_dict()
    assert fixture["randomness_policy"] == LAST_STAND_RANDOMNESS_POLICY
    assert fixture["contract"] == "last-stand-rules-targeted-v1"
    assert fixture["case_count"] == 5
    assert {case["id"] for case in fixture["cases"]} == {
        "last-stand-collect-on-entry",
        "last-stand-collision-revive",
        "last-stand-recovery-blocks-collision",
        "last-stand-starvation-revive",
        "last-stand-recovery-expiry",
    }
    cases = {case["id"]: case for case in fixture["cases"]}
    assert cases["last-stand-collect-on-entry"]["expected"]["last_stand_held"] is True
    revive = cases["last-stand-collision-revive"]["expected"]
    assert revive["alive"] is True
    assert revive["last_stand_held"] is False
    assert revive["recovery_ticks_remaining"] == 60
    assert len(revive["body"]) == 3


def test_last_stand_fixture_cli_detects_stale_output(tmp_path):
    output = tmp_path / "last_stand.json"
    assert main(["--output", str(output)]) == 0
    assert main(["--output", str(output), "--check"]) == 0
    output.write_text("{}\n", encoding="utf-8")
    assert main(["--output", str(output), "--check"]) == 1
