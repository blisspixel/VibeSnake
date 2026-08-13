"""Generate the Vibe Snake Open Knowledge Format 0.2 bundle."""

from __future__ import annotations

import argparse
import json
import re
from datetime import date, datetime, timezone
from pathlib import Path

GENERATOR_ACTOR = "process:vibesnake-okf-generator"
VERIFIER_ACTOR = "process:vibesnake-ci"


def _match(text: str, pattern: str, label: str) -> str:
    found = re.search(pattern, text, flags=re.DOTALL)
    if found is None:
        raise ValueError(f"Could not extract {label} from its canonical source.")
    return found.group(1)


def _frontmatter(
    concept_type: str,
    title: str,
    description: str,
    tags: list[str],
    sources: list[tuple[str, str, str]],
    generated_at: str,
    verified_at: str,
    stale_after: str,
) -> str:
    lines = [
        "---",
        f'type: "{concept_type}"',
        f'title: "{title}"',
        f'description: "{description}"',
        "tags: [" + ", ".join(tags) + "]",
        f"generated: {{ by: {GENERATOR_ACTOR}, at: {generated_at} }}",
        f"verified: {{ by: {VERIFIER_ACTOR}, at: {verified_at} }}",
        f'stale_after: "{stale_after}"',
        "status: draft",
        "sources:",
    ]
    for source_id, resource, source_title in sources:
        lines.extend(
            (
                f"  - id: {source_id}",
                f"    resource: {resource}",
                f'    title: "{source_title}"',
            )
        )
    lines.extend(("---", ""))
    return "\n".join(lines)


def render_bundle(repository_root: Path) -> dict[str, str]:
    """Render deterministic knowledge concepts from canonical code and manifests."""
    rules_identity_path = repository_root / "native/src/VibeSnake.Rules/RulesetIdentity.cs"
    contracts_path = repository_root / "native/src/VibeSnake.AgentPlay/AgentContracts.cs"
    experience_path = repository_root / "native/src/VibeSnake.AgentPlay/AgentExperience.cs"
    tools_path = repository_root / "native/tools/VibeSnake.AgentHost/McpAgentTools.cs"
    resources_path = repository_root / "native/tools/VibeSnake.AgentHost/AgentResources.cs"
    program_path = repository_root / "native/tools/VibeSnake.AgentHost/Program.cs"
    host_project_path = repository_root / "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj"
    plugin_path = repository_root / "integrations/vibesnake-agent-plugin/plugin.json"
    baseline_path = repository_root / "integrations/agent-interop-baseline.json"

    rules_identity = rules_identity_path.read_text(encoding="utf-8")
    contracts = contracts_path.read_text(encoding="utf-8")
    experience = experience_path.read_text(encoding="utf-8")
    tools = tools_path.read_text(encoding="utf-8")
    resources = resources_path.read_text(encoding="utf-8")
    program = program_path.read_text(encoding="utf-8")
    host_project = host_project_path.read_text(encoding="utf-8")
    plugin = json.loads(plugin_path.read_text(encoding="utf-8"))
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    okf = baseline["okf"]
    okf_version = okf["spec_version"]
    lifecycle = (
        okf["generated_at"],
        okf["verified_at"],
        okf["stale_after"],
    )

    ruleset_id = _match(rules_identity, r'CurrentId = "([^"]+)"', "ruleset ID")
    rules_version = _match(rules_identity, r"CurrentVersion = ([0-9]+)", "rules version")
    observation_schema = _match(
        contracts,
        r"record AgentObservationV1\(.*?Contract = \"([^\"]+)\"",
        "observation schema",
    )
    result_schema = _match(
        contracts,
        r"record AgentMatchResult\(.*?Contract = \"([^\"]+)\"",
        "result schema",
    )
    host_version = _match(program, r'HostVersion = "([^"]+)"', "host version")
    sdk_version = _match(
        host_project,
        r'<PackageReference Include="ModelContextProtocol" Version="([^"]+)"',
        "MCP SDK version",
    )
    tool_names = sorted(set(re.findall(r'Name = "([a-z_]+)"', tools)))
    resource_uris = sorted(set(re.findall(r'UriTemplate = "([^"]+)"', resources)))
    style_ids = re.findall(r'public const string \w+Id = "([a-z-]+)";', experience)
    lesson_ids = re.findall(r'\n\s+"([a-z-]+)",\n\s+"[^"]+",', experience)
    plugin_schema = plugin["$schema"]
    plugin_version = plugin["version"]

    index = "\n".join(
        (
            "---",
            f'okf_version: "{okf_version}"',
            "---",
            "",
            "# Vibe Snake Agent Knowledge",
            "",
            "* [Rules and observations](rules.md) - Public state, actions, modes, and authority boundaries.",
            "* [MCP protocol](protocol.md) - Local host tools, resources, versions, and transport limits.",
            "* [Agent experience](experience.md) - Signal School lessons and Style Contracts.",
            "* [Verified replay handoff](replays.md) - Verified results, explicit saving, and human viewing.",
            "",
        )
    )

    rules = _frontmatter(
        "Game Rules",
        "Vibe Snake agent rules and observations",
        "The public, deterministic rules boundary available to an external agent.",
        ["vibesnake", "rules", "observation", "agents"],
        [
            ("rules-identity", "../../native/src/VibeSnake.Rules/RulesetIdentity.cs", "Ruleset identity"),
            ("agent-contracts", "../../native/src/VibeSnake.AgentPlay/AgentContracts.cs", "Agent contracts"),
            ("mode-catalog", "../../native/src/VibeSnake.Rules/RunModeCatalog.cs", "Official mode catalog"),
        ],
        *lifecycle,
    ) + "\n".join(
        (
            "# Authority",
            "",
            f"The rules authority is `{ruleset_id}@{rules_version}`. The public observation schema is `{observation_schema}`.",
            "This knowledge bundle is descriptive. The rules assembly, tool schemas, and verified replay remain authoritative.",
            "",
            "# Actions",
            "",
            "An agent may choose `continue`, `up`, `right`, `down`, or `left`. In `four-direction-step-v1`, one accepted action advances exactly one clock-free rules step. In the separate `four-direction-burst-v1` division, one initial action is followed by at most 15 straight continuations and stops under fixed `decision-event-stop-v1` public events or a closed terminal, cap, replay-failure, or requested-bound reason.",
            "Each mutation is bound to the observed tick, state hash, and one shared idempotency-key namespace capped at 4,096 unique records per match. Exact retries return cached typed responses; known keys are never evicted, and changed, cross-operation, or post-cap unseen keys advance no additional state.",
            "",
            "# Public observation",
            "",
            "The observation includes the board, ordered body, direction queue, food, visible powers and obstacles, score, combo, hunger, active effects, adaptive policy, previous public events, episode metrics, and optional style progress.",
            "It excludes random state, future outcomes, controller internals, profiles, progression, paths, prompts, credentials, diagnostics, and hidden reasoning.",
            "",
            "# Seed divisions",
            "",
            "Open matches expose the gameplay seed. Blind matches withhold it until the verified result. Classic and Vibe results remain separate identities.",
            "",
        )
    )

    protocol = _frontmatter(
        "Protocol",
        "Vibe Snake MCP agent host",
        "The local stdio MCP surface and its portable Agent Plugin packaging.",
        ["vibesnake", "mcp", "agent-plugins", "stdio"],
        [
            ("mcp-tools", "../../native/tools/VibeSnake.AgentHost/McpAgentTools.cs", "MCP tool adapter"),
            ("mcp-resources", "../../native/tools/VibeSnake.AgentHost/AgentResources.cs", "MCP resources"),
            ("plugin-manifest", "../vibesnake-agent-plugin/plugin.json", "Agent Plugin manifest"),
            ("agent-plugins-spec", "https://agent-plugins.org/specification", "Agent Plugins 1.0.0 specification"),
        ],
        *lifecycle,
    ) + "\n".join(
        (
            "# Versions",
            "",
            f"The host version is `{host_version}`. The Agent Plugin version is `{plugin_version}` and targets `{plugin_schema}`.",
            f"The MCP server targets stable protocol `2026-07-28` through the official C# SDK `{sdk_version}`.",
            "Clients must speak the stateless MCP `2026-07-28` era: every request carries protocol metadata, optional discovery uses `server/discover`, and there is no protocol session. Legacy `initialize` handshakes are rejected and this preview provides no downlevel fallback.",
            "",
            "# Tools",
            "",
            *(f"* `{name}`" for name in tool_names),
            "",
            "# Resources",
            "",
            *(f"* `{uri}`" for uri in resource_uris),
            "",
            "# Trust boundary",
            "",
            "The first transport is local stdio. It opens no network listener, accepts no executable, arbitrary path, action list, or custom stop predicate, and keeps opaque bearer handles in one bounded process without a separate client-authentication layer. Finalized matches are evicted first at capacity; otherwise only a live handle with no valid handle-bearing operation for 30 minutes may be reclaimed without a result or replay. Replacement construction precedes eviction, and viewer activity is never match control. Agent Plugins packaging is preview-quality because its 1.0.0 specification remains a working draft.",
            "",
        )
    )

    experience_concept = _frontmatter(
        "Curriculum",
        "Vibe Snake Signal School and Style Contracts",
        "Deterministic lessons and self-selected public goals for agent-native play.",
        ["vibesnake", "curriculum", "styles", "evaluation"],
        [
            ("agent-experience", "../../native/src/VibeSnake.AgentPlay/AgentExperience.cs", "Agent experience catalog"),
            ("experience-design", "../../docs/design/AGENT_ARENA.md", "Agent Arena experience contract"),
        ],
        *lifecycle,
    ) + "\n".join(
        (
            "# Style Contracts",
            "",
            *(f"* `{style_id}`" for style_id in style_ids),
            "",
            "A style contract reports progress from public episode metrics. It does not change rules, scoring, spawn order, or replay verification.",
            "",
            "# Signal School",
            "",
            *(f"* `{lesson_id}`" for lesson_id in lesson_ids),
            "",
            "Lessons declare an official mode, practice seed, step cap, metric, and target. Qualification should use separate withheld blind seeds and versioned divisions.",
            "Bounded symbolic bursts reduce routine tool-call cost before lesson-selectable sessions are added, while preserving exact replay, metric, rival-step, and division identity.",
            "",
        )
    )

    replays = _frontmatter(
        "Replay Contract",
        "Verified agent replay handoff",
        "How successfully finalized agent play becomes a verified result and human-watchable replay.",
        ["vibesnake", "replay", "verification", "spectator"],
        [
            ("agent-session", "../../native/src/VibeSnake.AgentPlay/AgentMatchSession.cs", "Agent match owner"),
            ("replay-store", "../../native/src/VibeSnake.Persistence/ReplayStore.cs", "Bounded replay store"),
            ("replay-doc", "../../docs/engineering/REPLAYS.md", "Replay engineering contract"),
        ],
        *lifecycle,
    ) + "\n".join(
        (
            "# Verified result",
            "",
            f"A successfully finalized completed, capped, or explicitly finished match returns `{result_schema}` with final state hash, replay payload hash, rules and mode identity, outcome, metrics, and verification code. Failed-closed finalization returns neither a verified result nor a verified replay.",
            "",
            "# Persistence",
            "",
            "Replay saving is an explicit call into the bounded application-owned replay store. The agent supplies no path. The saved file is reloaded and verified before the existing replay presentation consumes it.",
            "",
            "# Human viewing",
            "",
            "The same replay browser and clock-free playback used for human runs can play the agent action trace at a human-selected pace. Playback presentation cannot alter the canonical final hash.",
            "",
        )
    )

    return {
        "index.md": index,
        "rules.md": rules,
        "protocol.md": protocol,
        "experience.md": experience_concept,
        "replays.md": replays,
    }


def write_bundle(output_root: Path, rendered: dict[str, str]) -> None:
    """Write the exact generated bundle without touching unrelated files."""
    output_root.mkdir(parents=True, exist_ok=True)
    for name, content in rendered.items():
        (output_root / name).write_text(content, encoding="utf-8", newline="\n")


def check_bundle(output_root: Path, rendered: dict[str, str]) -> tuple[str, ...]:
    """Return generated-bundle drift diagnostics."""
    problems: list[str] = []
    for name, content in rendered.items():
        path = output_root / name
        if not path.is_file():
            problems.append(f"missing generated file: {name}")
        elif path.read_text(encoding="utf-8") != content:
            problems.append(f"generated file is stale: {name}")
    extras = sorted(path.name for path in output_root.glob("*.md") if path.name not in rendered)
    problems.extend(f"unexpected generated concept: {name}" for name in extras)
    return tuple(problems)


def check_freshness(as_of: date, stale_after_value: str) -> tuple[str, ...]:
    """Return a deterministic diagnostic when the reviewed bundle is stale."""
    stale_after = date.fromisoformat(stale_after_value)
    if as_of >= stale_after:
        return (
            "agent knowledge is stale: "
            f"as-of {as_of.isoformat()} reached stale_after {stale_after_value}; "
            "review canonical sources and advance verification metadata",
        )
    return ()


def _parse_as_of(value: str) -> date:
    try:
        parsed = date.fromisoformat(value)
    except ValueError as exception:
        raise argparse.ArgumentTypeError("--as-of must be an absolute YYYY-MM-DD date") from exception
    if parsed.isoformat() != value:
        raise argparse.ArgumentTypeError("--as-of must be an absolute YYYY-MM-DD date")
    return parsed


def main() -> int:
    """Write or verify the repository knowledge bundle."""
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    parser.add_argument("--repository-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path)
    parser.add_argument(
        "--as-of",
        type=_parse_as_of,
        default=None,
        help="Date used for the OKF freshness gate; defaults to the current UTC date.",
    )
    arguments = parser.parse_args()
    repository_root = arguments.repository_root.resolve()
    output = arguments.output or repository_root / "integrations/vibesnake-agent-knowledge"
    baseline = json.loads((repository_root / "integrations/agent-interop-baseline.json").read_text(encoding="utf-8"))
    rendered = render_bundle(repository_root)
    if arguments.write:
        write_bundle(output, rendered)
        print(f"Generated OKF 0.2 bundle: {output.resolve()}")
        return 0
    as_of = arguments.as_of or datetime.now(timezone.utc).date()
    problems = check_bundle(output, rendered) + check_freshness(
        as_of,
        baseline["okf"]["stale_after"],
    )
    if problems:
        print("Agent knowledge check failed:")
        for problem in problems:
            print(f"  {problem}")
        return 1
    print(f"Agent knowledge check passed: {output.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
