import hashlib
import sys
from pathlib import Path

import pytest

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPOSITORY_ROOT / "scripts"))

from check_agent_interop_upstream import (  # noqa: E402
    BASELINE_RELATIVE_PATH,
    MAXIMUM_BASELINE_BYTES,
    check_upstream,
    load_baseline,
)


def test_checked_in_remote_pins_are_digest_bound_without_network() -> None:
    baseline = load_baseline(REPOSITORY_ROOT / BASELINE_RELATIVE_PATH)
    payload = b'{"$schema":"https://json-schema.org/draft/2020-12/schema"}'
    digest = hashlib.sha256(payload).hexdigest()
    baseline["agent_plugins"]["spec_source_sha256"] = digest
    baseline["agent_plugins"]["plugin_schema_sha256"] = digest
    baseline["agent_plugins"]["mcp_schema_sha256"] = digest

    assert check_upstream(baseline, fetch=lambda _url: payload) == ()
    baseline["agent_plugins"]["spec_source_sha256"] = "0" * 64
    errors = check_upstream(baseline, fetch=lambda _url: payload)
    assert len(errors) == 1
    assert "upstream specification digest changed" in errors[0]


def test_upstream_probe_rejects_incomplete_pins_and_reports_fetch_failures() -> None:
    baseline = load_baseline(REPOSITORY_ROOT / BASELINE_RELATIVE_PATH)
    baseline["agent_plugins"]["spec_source_url"] = "http://example.invalid/spec"
    baseline["agent_plugins"]["plugin_schema_sha256"] = "ABC"

    def fail(_url: str) -> bytes:
        raise TimeoutError("bounded timeout")

    errors = check_upstream(baseline, fetch=fail)

    assert "agent_plugins specification pin is incomplete" in errors
    assert "agent_plugins plugin schema pin is incomplete" in errors
    assert any("could not fetch mcp schema" in error for error in errors)
    assert check_upstream({}) == ("agent_plugins must be an object",)


def test_baseline_loader_rejects_duplicates_nonobjects_and_oversize(tmp_path: Path) -> None:
    path = tmp_path / "baseline.json"
    path.write_text('{"agent_plugins":{},"agent_plugins":{}}', encoding="utf-8")
    with pytest.raises(ValueError, match="duplicate JSON key"):
        load_baseline(path)

    path.write_text("[]", encoding="utf-8")
    with pytest.raises(ValueError, match="root must be an object"):
        load_baseline(path)

    path.write_bytes(b" " * (MAXIMUM_BASELINE_BYTES + 1))
    with pytest.raises(ValueError, match="exceeds 65536 bytes"):
        load_baseline(path)
