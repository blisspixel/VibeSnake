"""Campaign orchestration, determinism checks, and JSON reporting."""

from __future__ import annotations

import hashlib
import json
import random
from contextlib import contextmanager
from datetime import datetime, timezone
from statistics import fmean
from typing import Any, Iterator

from vibesnake.qa.invariants import check_invariants
from vibesnake.qa.models import (
    CampaignReport,
    InvariantFailure,
    Scenario,
    ScenarioResult,
    StepRecord,
)
from vibesnake.qa.policies import get_policy
from vibesnake.qa.simulation import CoreSimulation


def run_scenario(scenario: Scenario) -> ScenarioResult:
    """Run one seeded scenario against the reference core."""
    policy = get_policy(scenario.policy)
    policy_rng = random.Random(scenario.seed ^ 0x5EED5EED)
    records: list[StepRecord] = []
    actions: list[tuple[str, ...]] = []
    failures: list[InvariantFailure] = []

    with _seeded_global_random(scenario.seed):
        simulation = CoreSimulation(step_seconds=scenario.step_seconds)
        failures.extend(check_invariants(simulation))
        previous: StepRecord | None = None

        for _ in range(scenario.max_steps):
            if failures or not simulation.alive:
                break
            try:
                commands = policy(simulation, policy_rng)
                record = simulation.step(commands)
            except Exception as error:
                failures.append(
                    InvariantFailure(
                        code="runner.exception",
                        message=f"{type(error).__name__}: {error}",
                        step=simulation.step_count,
                    )
                )
                break

            records.append(record)
            actions.append(record.commands)
            failures.extend(check_invariants(simulation, record, previous))
            previous = record

    return ScenarioResult(
        scenario=scenario,
        passed=not failures,
        steps_executed=simulation.step_count,
        food_eaten=simulation.food_eaten,
        score=simulation.score.base_score,
        final_length=len(simulation.snake.body),
        wraps=simulation.wraps,
        won=simulation.won,
        death_cause=simulation.death_cause,
        trace_hash=_trace_hash(records),
        actions=actions,
        failures=failures,
    )


def run_campaign(
    scenarios: list[Scenario],
    *,
    verify_determinism: bool = True,
) -> CampaignReport:
    """Run scenarios and optionally replay each to detect divergence."""
    if not scenarios:
        raise ValueError("a campaign must contain at least one scenario")
    results: list[ScenarioResult] = []

    for scenario in scenarios:
        result = run_scenario(scenario)
        if verify_determinism and result.passed:
            replay = run_scenario(scenario)
            if replay.trace_hash != result.trace_hash or replay.actions != result.actions:
                result.failures.append(
                    InvariantFailure(
                        code="determinism.trace_diverged",
                        message=(f"replay hash {replay.trace_hash} did not match original hash {result.trace_hash}"),
                        step=min(result.steps_executed, replay.steps_executed),
                    )
                )
                result.passed = False
        results.append(result)

    return CampaignReport(
        generated_at_utc=datetime.now(timezone.utc).isoformat(),
        scenarios=results,
        aggregates=_aggregate(results),
    )


def report_json(report: CampaignReport, *, pretty: bool = True) -> str:
    """Serialize a campaign report with stable key ordering."""
    indent = 2 if pretty else None
    return json.dumps(report.to_dict(), indent=indent, sort_keys=True) + "\n"


def _trace_hash(records: list[StepRecord]) -> str:
    canonical = json.dumps(
        [record.canonical_dict() for record in records],
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def _aggregate(results: list[ScenarioResult]) -> dict[str, Any]:
    by_policy: dict[str, list[ScenarioResult]] = {}
    for result in results:
        by_policy.setdefault(result.scenario.policy, []).append(result)

    policy_summaries: dict[str, Any] = {}
    for policy, policy_results in sorted(by_policy.items()):
        policy_summaries[policy] = {
            "scenarios": len(policy_results),
            "passed": sum(result.passed for result in policy_results),
            "mean_steps": fmean(result.steps_executed for result in policy_results),
            "mean_food": fmean(result.food_eaten for result in policy_results),
            "mean_score": fmean(result.score for result in policy_results),
            "outcomes": _counts(
                "won" if result.won else result.death_cause or "step_limit" for result in policy_results
            ),
        }

    return {
        "scenarios": len(results),
        "passed": sum(result.passed for result in results),
        "failed": sum(not result.passed for result in results),
        "by_policy": policy_summaries,
    }


def _counts(values: Iterator[str]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for value in values:
        counts[value] = counts.get(value, 0) + 1
    return dict(sorted(counts.items()))


@contextmanager
def _seeded_global_random(seed: int):
    """Temporarily seed legacy global randomness without leaking state."""
    previous_state = random.getstate()
    random.seed(seed)
    try:
        yield
    finally:
        random.setstate(previous_state)
