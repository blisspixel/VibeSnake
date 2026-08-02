"""Canonical identity for scored gameplay behavior."""

from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Any


@dataclass(frozen=True)
class RulesetIdentity:
    """Stable gameplay-rules identity independent of the host runtime."""

    id: str
    version: int

    def __post_init__(self) -> None:
        if not isinstance(self.id, str) or not self.id.strip():
            raise ValueError("ruleset id must be a nonblank string")
        if isinstance(self.version, bool) or not isinstance(self.version, int) or self.version <= 0:
            raise ValueError("ruleset version must be a positive integer")

    @property
    def contract_id(self) -> str:
        """Return the compact identity used in diagnostics and documentation."""
        return f"{self.id}@{self.version}"

    def to_dict(self) -> dict[str, Any]:
        """Return a fresh JSON-compatible representation."""
        return asdict(self)


CURRENT_RULESET = RulesetIdentity(id="vibesnake-core", version=4)
