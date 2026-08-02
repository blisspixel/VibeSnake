"""Versioned metadata shared by deterministic QA contracts."""

from __future__ import annotations

from vibesnake.core.ruleset import CURRENT_RULESET, RulesetIdentity


SHARED_RANDOMNESS_POLICY = "positions-injected-or-random-output-normalized-v2"

__all__ = ["CURRENT_RULESET", "SHARED_RANDOMNESS_POLICY", "RulesetIdentity"]
