"""Tests for descriptive run-local death telemetry."""

import pytest

from vibesnake.core.enums import DeathCause
from vibesnake.core.metrics import MetricsTracker


def test_initial_state_reports_zeroed_categories():
    metrics = MetricsTracker()

    assert metrics.get_death_statistics() == {
        "total_deaths": 0,
        "collision_deaths": 0,
        "starvation_deaths": 0,
        "collision_percent": 0.0,
        "starvation_percent": 0.0,
    }


def test_default_cause_is_collision():
    metrics = MetricsTracker()

    metrics.record_death(42)

    assert metrics.get_death_statistics() == {
        "total_deaths": 1,
        "collision_deaths": 1,
        "starvation_deaths": 0,
        "collision_percent": 100.0,
        "starvation_percent": 0.0,
    }


def test_death_statistics_report_causes_without_balance_judgment():
    metrics = MetricsTracker()
    metrics.record_death(10, DeathCause.COLLISION)
    metrics.record_death(20, DeathCause.COLLISION)
    metrics.record_death(30, DeathCause.STARVATION)

    statistics = metrics.get_death_statistics()

    assert statistics == {
        "total_deaths": 3,
        "collision_deaths": 2,
        "starvation_deaths": 1,
        "collision_percent": 66.7,
        "starvation_percent": 33.3,
    }
    assert "balance_status" not in statistics


def test_unsupported_cause_does_not_mutate_totals():
    metrics = MetricsTracker()

    with pytest.raises(ValueError, match="unsupported death cause"):
        metrics.record_death(7, object())

    assert metrics.deaths_this_session == 0
    assert metrics.collision_deaths == 0
    assert metrics.starvation_deaths == 0


def test_new_tracker_does_not_share_previous_run_counts():
    prior_run = MetricsTracker()
    prior_run.record_death(5, DeathCause.STARVATION)

    next_run = MetricsTracker()

    assert next_run.deaths_this_session == 0
    assert prior_run.deaths_this_session == 1
