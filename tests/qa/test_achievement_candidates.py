"""Dual-runtime achievement candidate evaluation and gated emission."""

from __future__ import annotations

from vibesnake.qa.achievement_candidates import (
    DEFINITIONS,
    RunAchievementMetrics,
    evaluate_candidates,
    index_of,
)
from vibesnake.qa.simulation import CoreSimulation


def test_catalog_order_matches_native_first_ids() -> None:
    # Keep aligned with VibeSnake.Rules.AchievementCatalog.Definitions.
    expected_ids = (
        "first_bite",
        "century",
        "high_roller",
        "legend",
        "just_a_taste",
        "getting_longer",
        "growing_strong",
        "serpent",
        "combo_starter",
        "combo_king",
        "wrap_around",
        "close_call",
        "powered_up",
        "power_hungry",
        "quick_reflexes",
        "endurance",
        "marathon",
    )
    assert tuple(definition.id for definition in DEFINITIONS) == expected_ids
    assert index_of("century") == 1
    assert index_of("marathon") == 16
    assert index_of("missing_id") == -1


def test_evaluate_candidates_requires_terminal_by_default() -> None:
    metrics = RunAchievementMetrics(
        score=150,
        max_combo=6,
        length=3,
        food_eaten=2,
        wrap_count=0,
        near_misses=0,
        powerups_collected=0,
        survival_ticks=10,
        is_terminal=False,
    )
    assert evaluate_candidates(metrics) == []
    earned = evaluate_candidates(metrics, require_terminal=False)
    assert "first_bite" in earned
    assert "century" in earned
    assert "combo_starter" in earned


def test_core_simulation_skips_candidates_when_flag_default_off() -> None:
    sim = CoreSimulation(step_seconds=0.05)
    sim.starvation_limit_seconds = 0.05
    sim.score.base_score = 150
    record = sim.step(())
    assert not sim.alive
    assert record.death_cause == "starvation"
    assert all(event.kind != "achievement_candidate" for event in record.events)


def test_core_simulation_emits_candidates_when_flag_enabled() -> None:
    sim = CoreSimulation(
        step_seconds=0.05,
        enable_achievement_candidates=True,
    )
    sim.starvation_limit_seconds = 0.05
    sim.score.base_score = 150
    sim.session_max_combo = 6
    record = sim.step(())
    assert not sim.alive
    candidate_values = [event.value for event in record.events if event.kind == "achievement_candidate"]
    assert index_of("first_bite") in candidate_values
    assert index_of("century") in candidate_values


def test_core_simulation_suppresses_already_unlocked_candidates() -> None:
    sim = CoreSimulation(
        step_seconds=0.05,
        enable_achievement_candidates=True,
        already_unlocked_achievements=frozenset({"first_bite"}),
    )
    sim.starvation_limit_seconds = 0.05
    sim.score.base_score = 150
    record = sim.step(())
    assert not sim.alive
    candidate_values = [event.value for event in record.events if event.kind == "achievement_candidate"]
    assert index_of("first_bite") not in candidate_values
    assert index_of("century") in candidate_values
