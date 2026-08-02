"""Contracts for the Python-to-C# movement fixture."""

from pathlib import Path

import pytest

from vibesnake.qa.shared_traces import (
    DEFAULT_CASE_COUNT,
    DEFAULT_STEPS_PER_CASE,
    DIRECTION_SYMBOLS,
    STEP_ENCODING,
    build_movement_fixture,
    check_fixture,
    main,
)
from vibesnake.qa.contracts import CURRENT_RULESET, SHARED_RANDOMNESS_POLICY


FIXTURE_PATH = Path(__file__).parents[1] / "fixtures" / "shared" / "core_movement_v2.json"


def test_checked_in_shared_fixture_matches_production_movement():
    fixture = build_movement_fixture()

    assert check_fixture(FIXTURE_PATH, fixture)
    assert fixture["ruleset"] == CURRENT_RULESET.to_dict()
    assert fixture["randomness_policy"] == SHARED_RANDOMNESS_POLICY
    assert fixture["case_count"] == DEFAULT_CASE_COUNT
    assert fixture["schema_version"] == 2
    assert fixture["contract"] == "movement-input-long-v2"
    assert fixture["step_encoding"] == list(STEP_ENCODING)
    assert fixture["direction_symbols"] == DIRECTION_SYMBOLS
    assert DEFAULT_STEPS_PER_CASE == 256
    assert [case["seed"] for case in fixture["cases"]] == list(range(DEFAULT_CASE_COUNT))
    assert [case["id"] for case in fixture["cases"]] == [
        f"movement-seed-{seed:03d}" for seed in range(DEFAULT_CASE_COUNT)
    ]
    assert all(len(case["steps"]) == DEFAULT_STEPS_PER_CASE for case in fixture["cases"])
    steps = [step for case in fixture["cases"] for step in case["steps"]]
    assert sum(step[7] for step in steps) >= DEFAULT_CASE_COUNT
    assert max(len(step[6]) for step in steps) == 2
    assert max(step[1].count("1") for step in steps) == 3
    assert sum("1110" in step[1] for step in steps) > DEFAULT_CASE_COUNT
    assert sum(len(step[0]) for step in steps) > 10_000
    assert sum(bits.count("0") for bits in (step[1] for step in steps)) > 5_000
    assert all(len(step) == len(STEP_ENCODING) for step in steps)
    assert all(len(step[0]) == len(step[1]) for step in steps)


def test_shared_fixture_cli_detects_stale_output(tmp_path):
    output = tmp_path / "movement.json"

    assert main(["--output", str(output), "--cases", "2", "--steps", "4"]) == 0
    assert main(["--output", str(output), "--cases", "2", "--steps", "4", "--check"]) == 0
    output.write_text("{}\n", encoding="utf-8")
    assert main(["--output", str(output), "--cases", "2", "--steps", "4", "--check"]) == 1


@pytest.mark.parametrize("case_count,steps", [(0, 1), (1, 0)])
def test_shared_fixture_rejects_empty_corpus(case_count, steps):
    with pytest.raises(ValueError, match="greater than zero"):
        build_movement_fixture(case_count=case_count, steps_per_case=steps)
