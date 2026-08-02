"""Composable movement-cadence modifiers for temporary power effects."""

from __future__ import annotations

from typing import Protocol

from vibesnake.data import settings


class CadenceGame(Protocol):
    """Game state required to compose temporary cadence factors."""

    logic_tick_override: float | None


def set_cadence_factor(game: CadenceGame, effect: str, factor: float) -> None:
    """Set one positive cadence factor and recompute the effective interval."""
    if factor <= 0:
        raise ValueError("cadence factor must be positive")
    factors = _factors(game)
    factors[effect] = factor
    _apply(game, factors)


def clear_cadence_factor(game: CadenceGame, effect: str) -> None:
    """Remove one cadence factor without clearing overlapping effects."""
    factors = _factors(game)
    factors.pop(effect, None)
    _apply(game, factors)


def clear_cadence_factors(game: CadenceGame) -> None:
    """Clear every temporary cadence factor during run reset."""
    setattr(game, "_logic_tick_factors", {})
    game.logic_tick_override = None


def _factors(game: CadenceGame) -> dict[str, float]:
    factors = getattr(game, "_logic_tick_factors", None)
    if factors is None:
        factors = {}
        setattr(game, "_logic_tick_factors", factors)
    return factors


def _apply(game: CadenceGame, factors: dict[str, float]) -> None:
    if not factors:
        game.logic_tick_override = None
        return

    combined_factor = 1.0
    for factor in factors.values():
        combined_factor *= factor
    game.logic_tick_override = settings.LOGIC_TICK * combined_factor
