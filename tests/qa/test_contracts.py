"""Contracts for shared deterministic rules identity."""

import pytest

from vibesnake.qa.contracts import (
    CURRENT_RULESET,
    SHARED_RANDOMNESS_POLICY,
    RulesetIdentity,
)


def test_current_ruleset_identity_is_explicit_and_stable():
    assert CURRENT_RULESET.id == "vibesnake-core"
    assert CURRENT_RULESET.version == 4
    assert CURRENT_RULESET.contract_id == "vibesnake-core@4"
    assert CURRENT_RULESET.to_dict() == {"id": "vibesnake-core", "version": 4}
    assert SHARED_RANDOMNESS_POLICY == ("positions-injected-or-random-output-normalized-v2")


@pytest.mark.parametrize(
    "ruleset_id,version",
    [
        ("", 1),
        (" ", 1),
        (None, 1),
        (7, 1),
        (True, 1),
        ("valid", 0),
        ("valid", -1),
        ("valid", None),
        ("valid", True),
        ("valid", 1.5),
        ("valid", "1"),
    ],
)
def test_ruleset_identity_rejects_invalid_values(ruleset_id, version):
    with pytest.raises(ValueError):
        RulesetIdentity(ruleset_id, version)
