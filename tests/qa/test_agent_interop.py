import hashlib
import json
import shutil
from datetime import date
from pathlib import Path

from scripts.check_agent_interop import (
    BASELINE_RELATIVE_PATH,
    calculate_contract_digests,
    check_baseline,
    check_upstream,
    load_baseline,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
REQUIRED_FILES = (
    BASELINE_RELATIVE_PATH,
    Path("native/tools/VibeSnake.AgentHost/Program.cs"),
    Path("native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj"),
    Path("integrations/vibesnake-agent-plugin/plugin.json"),
    Path("scripts/package_agent_plugin.ps1"),
    Path("docs/engineering/AGENT_PLAY.md"),
    Path("native/src/VibeSnake.AgentPlay/AgentBurstPolicy.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentContracts.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentIdentity.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentExperience.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentMatchSession.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentObservationProjector.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentStyleEvidence.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentViewer.cs"),
    Path("native/src/VibeSnake.Rules/CosmeticSetCatalog.cs"),
    Path("native/src/VibeSnake.Rules/StationIdentityCatalog.cs"),
    Path("native/tools/VibeSnake.AgentHost/AgentHostContracts.cs"),
    Path("native/tools/VibeSnake.AgentHost/AgentResources.cs"),
    Path("native/tools/VibeSnake.AgentHost/AgentSessionRegistry.cs"),
    Path("native/tools/VibeSnake.AgentHost/AgentToolArgumentFilter.cs"),
    Path("native/tools/VibeSnake.AgentHost/AgentViewerServer.cs"),
    Path("native/tools/VibeSnake.AgentHost/McpAgentTools.cs"),
    Path("integrations/vibesnake-agent-plugin/skills/play-vibesnake/SKILL.md"),
)


def copy_contract_fixture(target: Path) -> None:
    for relative in REQUIRED_FILES:
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(REPOSITORY_ROOT / relative, destination)


def test_checked_in_interoperability_baseline_is_aligned_and_fresh() -> None:
    assert check_baseline(REPOSITORY_ROOT, date(2026, 8, 15)) == ()
    baseline = load_baseline(REPOSITORY_ROOT / BASELINE_RELATIVE_PATH)
    assert baseline["mcp"] == {
        "protocol_version": "2026-07-28",
        "sdk_package": "ModelContextProtocol",
        "sdk_version": "2.2.0",
        "host_version": "0.16.0",
        "transport": "stdio",
        "session_model": "stateless",
    }
    assert baseline["agent_plugins"]["spec_version"] == "1.0.0"
    assert baseline["agent_plugins"]["plugin_version"] == "0.16.0"
    assert baseline["okf"]["spec_version"] == "0.2"
    assert baseline["public_contract_history"]["host"][-1]["version"] == "0.16.0"
    assert baseline["public_contract_history"]["plugin"][-1]["version"] == "0.16.0"


def test_interoperability_baseline_rejects_staleness_and_source_drift(tmp_path: Path) -> None:
    copy_contract_fixture(tmp_path)
    baseline_path = tmp_path / BASELINE_RELATIVE_PATH
    baseline = load_baseline(baseline_path)
    baseline["mcp"]["host_version"] = "0.1.0"
    baseline["okf"]["stale_after"] = "2026-11-14T00:00:00Z"
    baseline_path.write_text(json.dumps(baseline, indent=2) + "\n", encoding="utf-8")

    errors = check_baseline(tmp_path, date(2026, 11, 14))

    assert any("interoperability baseline is stale" in error for error in errors)
    assert "okf.stale_after must be an absolute YYYY-MM-DD date" in errors
    assert any("mcp.host_version='0.1.0'" in error for error in errors)


def test_upstream_specification_and_schema_check_is_digest_bound_without_network() -> None:
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


def test_baseline_rejects_unreviewed_surface_values_and_non_datetime_metadata(tmp_path: Path) -> None:
    copy_contract_fixture(tmp_path)
    baseline_path = tmp_path / BASELINE_RELATIVE_PATH
    baseline = load_baseline(baseline_path)
    baseline["mcp"]["transport"] = "http"
    baseline["mcp"]["session_model"] = "sessionful"
    baseline["agent_plugins"]["normative_status"] = "draft"
    baseline["agent_plugins"]["website_status"] = "published"
    baseline["agent_plugins"]["spec_source_commit"] = "main"
    baseline["agent_skill"]["fields"] = ["name"]
    baseline["mcp_apps"]["status"] = "supported"
    baseline["okf"]["generated_at"] = "2026-08-13Z"
    baseline_path.write_text(json.dumps(baseline, indent=2) + "\n", encoding="utf-8")

    errors = check_baseline(tmp_path, date(2026, 8, 13))

    assert "mcp.transport must be 'stdio'" in errors
    assert "mcp.session_model must be 'stateless'" in errors
    assert "agent_plugins.normative_status must be 'published'" in errors
    assert "agent_plugins.website_status must be 'working-draft'" in errors
    assert "agent_plugins.spec_source_commit must be a full lowercase Git commit SHA" in errors
    assert "agent_plugins.spec_source_url must bind the reviewed version to its immutable commit" in errors
    assert "agent_skill.fields must be the reviewed minimal ordered field set" in errors
    assert "mcp_apps.status must be 'tracked-only'" in errors
    assert "okf.generated_at must be a canonical RFC 3339 UTC datetime" in errors


def test_public_contract_change_requires_a_new_versioned_digest_entry(tmp_path: Path) -> None:
    copy_contract_fixture(tmp_path)
    resources = tmp_path / "native/tools/VibeSnake.AgentHost/AgentResources.cs"
    resources.write_text(
        resources.read_text(encoding="utf-8") + "\n// public resource drift\n",
        encoding="utf-8",
    )

    errors = check_baseline(tmp_path, date(2026, 8, 13))

    assert "public host contract changed without a matching versioned digest entry" in errors
    assert (
        calculate_contract_digests(tmp_path)["host"]
        != load_baseline(tmp_path / BASELINE_RELATIVE_PATH)["public_contract_history"]["host"][-1]["sha256"]
    )


def test_public_contract_history_requires_strictly_increasing_versions(tmp_path: Path) -> None:
    copy_contract_fixture(tmp_path)
    baseline_path = tmp_path / BASELINE_RELATIVE_PATH
    baseline = load_baseline(baseline_path)
    baseline["public_contract_history"]["host"][-1]["version"] = "0.1.0"
    baseline["mcp"]["host_version"] = "0.1.0"
    baseline_path.write_text(json.dumps(baseline, indent=2) + "\n", encoding="utf-8")

    errors = check_baseline(tmp_path, date(2026, 8, 13))

    assert any("must be greater than the preceding history version" in error for error in errors)
