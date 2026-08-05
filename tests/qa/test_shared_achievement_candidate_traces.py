"""Contracts for dual-runtime achievement_candidate fixtures (PD-009 product path)."""

from pathlib import Path

from vibesnake.qa.contracts import CURRENT_RULESET, SHARED_RANDOMNESS_POLICY
from vibesnake.qa.shared_achievement_candidate_traces import (
    build_achievement_candidate_fixture,
    check_fixture,
)

FIXTURE_PATH = Path(__file__).parents[1] / "fixtures" / "shared" / "achievement_candidates_rules_v1.json"


def test_checked_in_achievement_candidate_fixture_matches_production():
    fixture = build_achievement_candidate_fixture()

    assert check_fixture(FIXTURE_PATH, fixture)
    assert fixture["ruleset"] == CURRENT_RULESET.to_dict()
    assert fixture["randomness_policy"] == SHARED_RANDOMNESS_POLICY
    assert fixture["schema_version"] == 1
    assert fixture["contract"] == "achievement-candidates-targeted-v1"
    assert fixture["case_count"] == 4
    assert fixture["config"]["enable_achievement_candidates"] is True
    assert {case["id"] for case in fixture["cases"]} == {
        "starvation-score-candidates",
        "starvation-suppresses-already-unlocked",
        "starvation-zero-score-no-candidates",
        "self-collision-score-candidates",
    }

    cases = {case["id"]: case for case in fixture["cases"]}
    scored = cases["starvation-score-candidates"]["expected"]["events"]
    assert {"kind": "achievement_candidate", "value": 0} in scored
    assert {"kind": "achievement_candidate", "value": 1} in scored

    suppressed = cases["starvation-suppresses-already-unlocked"]["expected"]["events"]
    assert all(event["kind"] != "achievement_candidate" for event in suppressed)

    zero = cases["starvation-zero-score-no-candidates"]["expected"]["events"]
    assert all(event["kind"] != "achievement_candidate" for event in zero)

    collision = cases["self-collision-score-candidates"]["expected"]["events"]
    assert {"kind": "achievement_candidate", "value": 0} in collision
    assert {"kind": "achievement_candidate", "value": 1} in collision
