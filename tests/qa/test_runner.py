"""Tests for reproducible QA scenarios and campaign reports."""

import json
import logging
import random

import pytest

from vibesnake.qa.cli import main
from vibesnake.qa.models import Scenario
from vibesnake.qa.runner import report_json, run_campaign, run_scenario


def test_same_scenario_reproduces_actions_and_trace_hash():
    scenario = Scenario(policy="food-seeking", seed=31, max_steps=250)

    first = run_scenario(scenario)
    second = run_scenario(scenario)

    assert first.passed
    assert first.trace_hash == second.trace_hash
    assert first.actions == second.actions
    assert first.food_eaten > 0


def test_runner_restores_process_random_state():
    random.seed(32)
    state_before = random.getstate()

    run_scenario(Scenario(policy="survival", seed=32, max_steps=20))

    assert random.getstate() == state_before


def test_campaign_aggregates_policies_and_serializes():
    report = run_campaign(
        [
            Scenario(policy="food-seeking", seed=40, max_steps=100),
            Scenario(policy="input-chaos", seed=40, max_steps=100),
        ]
    )

    payload = json.loads(report_json(report))

    assert report.passed
    assert payload["passed"] is True
    assert payload["schema_version"] == 2
    assert payload["aggregates"]["scenarios"] == 2
    assert set(payload["aggregates"]["by_policy"]) == {"food-seeking", "input-chaos"}
    assert payload["scenarios"][0]["scenario"]["scenario_id"].startswith("food-seeking:seed-40")


def test_cli_writes_a_ci_report(tmp_path):
    output = tmp_path / "campaign.json"
    package_logger = logging.getLogger("vibesnake")
    previous_level = package_logger.level
    package_logger.setLevel(logging.DEBUG)
    try:
        status = main(
            [
                "--seeds",
                "50",
                "--policies",
                "food-seeking",
                "--steps",
                "25",
                "--output",
                str(output),
            ]
        )
        assert package_logger.level == logging.DEBUG
    finally:
        package_logger.setLevel(previous_level)

    assert status == 0
    assert json.loads(output.read_text(encoding="utf-8"))["passed"] is True


def test_scenario_contract_rejects_non_work():
    with pytest.raises(ValueError, match="policy"):
        Scenario(policy=" ", seed=1)
    with pytest.raises(ValueError, match="seed"):
        Scenario(policy="survival", seed=True)
    with pytest.raises(ValueError, match="max_steps"):
        Scenario(policy="survival", seed=1, max_steps=0)
    with pytest.raises(ValueError, match="step_seconds"):
        Scenario(policy="survival", seed=1, step_seconds=0)
    with pytest.raises(ValueError, match="step_seconds"):
        Scenario(policy="survival", seed=1, step_seconds=float("nan"))


def test_campaign_rejects_an_empty_workload():
    with pytest.raises(ValueError, match="at least one scenario"):
        run_campaign([])
