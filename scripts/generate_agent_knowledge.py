"""Generate the Vibe Snake Open Knowledge Format 0.2 bundle."""

from __future__ import annotations

import argparse
import json
import re
from datetime import date, datetime, timezone
from pathlib import Path

GENERATOR_ACTOR = "process:vibesnake-okf-generator"
VERIFIER_ACTOR = "process:vibesnake-quality-gate"


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
    lesson_evidence_path = repository_root / "native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs"
    tools_path = repository_root / "native/tools/VibeSnake.AgentHost/McpAgentTools.cs"
    resources_path = repository_root / "native/tools/VibeSnake.AgentHost/AgentResources.cs"
    program_path = repository_root / "native/tools/VibeSnake.AgentHost/Program.cs"
    host_project_path = repository_root / "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj"
    plugin_path = repository_root / "integrations/vibesnake-agent-plugin/plugin.json"
    baseline_path = repository_root / "integrations/agent-interop-baseline.json"

    rules_identity = rules_identity_path.read_text(encoding="utf-8")
    contracts = contracts_path.read_text(encoding="utf-8")
    experience = experience_path.read_text(encoding="utf-8")
    lesson_evidence = lesson_evidence_path.read_text(encoding="utf-8")
    tools = tools_path.read_text(encoding="utf-8")
    resources = resources_path.read_text(encoding="utf-8")
    program = program_path.read_text(encoding="utf-8")
    host_project = host_project_path.read_text(encoding="utf-8")
    plugin = json.loads(plugin_path.read_text(encoding="utf-8"))
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    agent_plugins = baseline["agent_plugins"]
    mcp = baseline["mcp"]
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
        r"record AgentObservationV5\(.*?Contract = \"([^\"]+)\"",
        "observation schema",
    )
    result_schema = _match(
        contracts,
        r"record AgentMatchResultV5\(.*?Contract = \"([^\"]+)\"",
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
    style_catalog = _match(
        experience,
        r"public static class AgentStyleContractCatalog\s*\{(.*?)\n\}",
        "style catalog",
    )
    style_ids = re.findall(r'public const string \w+Id = "([a-z-]+)";', style_catalog)
    lesson_catalog = _match(
        lesson_evidence,
        r"public static class AgentSignalSchoolCatalog\s*\{(.*?)\n\}",
        "Signal School catalog",
    )
    lesson_constants = dict(
        re.findall(
            r'public const string (\w+Id) = "([a-z-]+)";',
            lesson_catalog,
        )
    )
    lesson_ids = [lesson_constants[symbol] for symbol in re.findall(r"\bLesson\(\s*(\w+Id)", lesson_catalog)]
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
            ("agent-identity", "../../native/src/VibeSnake.AgentPlay/AgentIdentity.cs", "Agent identity catalogs"),
            (
                "station-identity",
                "../../native/src/VibeSnake.Rules/StationIdentityCatalog.cs",
                "Station identity catalog",
            ),
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
            "An agent may choose `continue`, `up`, `right`, `down`, or `left`. In `four-direction-step-v1`, one accepted action advances exactly one clock-free rules step. In the separate `four-direction-burst-v1` division, one initial action is followed by at most 15 straight continuations and stops under fixed `decision-event-stop-v1` public events, a selected lesson's transition to all requirements reached, or a closed terminal, cap, replay-failure, or requested-bound reason.",
            "Each mutation is bound to the observed tick, state hash, and one shared idempotency-key namespace capped at 4,096 unique records per match. Exact retries return cached typed responses; known keys are never evicted, and changed, cross-operation, or post-cap unseen keys advance no additional state.",
            "",
            "# Public observation",
            "",
            "The observation includes the catalog-validated public Agent Passport v4, board, ordered body, direction queue, food, visible powers and obstacles, score, combo, hunger, active effects, adaptive policy, previous public events, episode metrics, optional two-criterion live style progress, and optional ordered Signal School requirement progress.",
            "Passport identity is caller-declared and ephemeral. Avatar, accent, and station IDs must resolve through the host's closed identity resource; they affect presentation only and remain independent of human progression and cosmetics.",
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
            ("viewer-contract", "../../native/src/VibeSnake.AgentPlay/AgentViewer.cs", "Live viewer wire contract"),
            ("viewer-client", "../../native/src/VibeSnake.AgentViewer/AgentViewerClient.cs", "Live viewer client"),
            ("plugin-manifest", "../vibesnake-agent-plugin/plugin.json", "Agent Plugin manifest"),
            (
                "agent-plugins-normative-spec",
                agent_plugins["spec_source_url"],
                "Immutable Agent Plugins 1.0.0 normative specification",
            ),
            (
                "agent-plugins-website",
                "https://agent-plugins.org/specification",
                "Agent Plugins public specification website",
            ),
            (
                "mcp-specification",
                f"https://modelcontextprotocol.io/specification/{mcp['protocol_version']}",
                f"Model Context Protocol {mcp['protocol_version']} specification",
            ),
            (
                "mcp-csharp-sdk",
                f"https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v{mcp['sdk_version']}",
                f"Official C# SDK {mcp['sdk_version']} release",
            ),
        ],
        *lifecycle,
    ) + "\n".join(
        (
            "# Versions",
            "",
            f"The host version is `{host_version}`. The Agent Plugin version is `{plugin_version}` and targets `{plugin_schema}`.",
            f"The MCP server targets stable protocol `{mcp['protocol_version']}` through the official C# SDK `{sdk_version}`.",
            f"Clients must speak the stateless MCP `{mcp['protocol_version']}` era: every request carries protocol metadata, optional discovery uses `server/discover`, and there is no protocol session. Legacy `initialize` handshakes are rejected and this preview provides no downlevel fallback.",
            "",
            "# Tools",
            "",
            *(f"* `{name}`" for name in tool_names),
            "",
            "# Resources",
            "",
            *(f"* `{uri}`" for uri in resource_uris),
            "",
            "# Live viewer",
            "",
            "The optional same-user pipe uses `vibesnake-agent-viewer-frame-v7`. Every frame declares initial, step, burst, or finish origin and binds exact steps advanced to the pre-mutation tick and state hash. Burst frames carry closed stop reason and final-step event, while terminal truth, immutable match identity, catalog-bound Passport v4, action facts, contiguous state anchors, two ordered live style criteria, ordered lesson progress, optional replay-bound terminal style outcomes, and optional combined-evidence lesson outcomes are cross-validated before presentation. Malformed, oversized, contradictory, unknown-catalog, identity-drifting, criterion-drifting, or mixed-version input clears pending content and rejects the stream. The host keeps only the latest unsent frame, the client reports sequence gaps as coalesced earlier updates, and the packaged-host transcript exercises rejection-aware lesson recovery as well as terminal burst delivery. The verified replay remains the canonical accepted-step history, and viewer timing never advances rules or score.",
            "",
            "# Trust boundary",
            "",
            "The first transport is local stdio. It opens no network listener, accepts no executable, arbitrary path, action list, or custom stop predicate, and keeps opaque bearer handles in one bounded process without a separate client-authentication layer. Finalized matches are evicted first at capacity; otherwise only a live handle with no valid handle-bearing operation for 30 minutes may be reclaimed without a result or replay. Replacement construction precedes eviction, and viewer activity is never match control. The normative Agent Plugins repository labels 1.0.0 Published while the public specification website still labels it Working Draft, so Vibe Snake retains preview-quality packaging and drift review.",
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
            (
                "lesson-evidence",
                "../../native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs",
                "Signal School requirement and evidence evaluator",
            ),
            (
                "style-evidence",
                "../../native/src/VibeSnake.AgentPlay/AgentStyleEvidence.cs",
                "Replay-derived style evidence evaluator",
            ),
            ("experience-design", "../../docs/design/AGENT_ARENA.md", "Agent Arena experience contract"),
        ],
        *lifecycle,
    ) + "\n".join(
        (
            "# Style Contracts",
            "",
            *(f"* `{style_id}`" for style_id in style_ids),
            "",
            "Each style publishes exactly two ordered, factual criteria under `replay-composite-core4-v1`. Stillwater combines rules-advanced-step survival with structural-open-exit rate. Crownchaser combines peak combo with uninterrupted food continuity through the first combo of four. Edge Prophet combines rewarded body-proximity near misses with a same-step wrap fact under the pinned `vibesnake-core@4` evaluator. Mutagenist combines distinct activated power kinds with concurrent active power kinds. Redline combines food count with safe progress toward the exact pre-step visible food.",
            "Live style values are rules-advanced-step observations and may rise or fall. Rate criteria expose integer numerators and denominators and use floor basis points. Successful finalization independently reconstructs the same facts from the verified replay, requires agreement with live evidence, and binds the terminal style outcome to the replay payload hash. These facts do not prove intent, planning, mastery, personality, or spectator appeal. A style never changes rules, scoring, spawn order, or replay verification.",
            "",
            "# Signal School",
            "",
            *(f"* `{lesson_id}`" for lesson_id in lesson_ids),
            "",
            "Call `start_lesson` with one of eight published lesson IDs to create its canonical open-seed practice session. Every definition publishes ordered closed requirements under `ordered-replay-attempt-evidence-v2`; observations return live requirement progress and the first unmet requirement, accepted moves and bursts return exact progress deltas, and verified finalization returns a factual outcome. A completed practice is not mastery or qualification.",
            "Accepted-step facts are independently reconstructed from the verified replay. The rejection-aware first-turn lesson additionally uses a maximum-32 canonical attempt-witness sequence: exact idempotent retries do not add evidence, and stale, conflicting, capacity, or wrong-profile requests cannot qualify. The outcome binds the replay payload hash and distinct attempt-evidence hash into one evidence hash. An ordinary saved replay contains only accepted-step history, so it cannot later prove the rejected reversal without a future receipt that carries the attempt evidence.",
            "A verified miss names the first unmet requirement and a closed review code. Failed-closed evidence produces no verified lesson outcome and directs the client to a fresh same-lesson `start_lesson` session without inherited rules state, mutation keys, or practice history. The resource also publishes exact action-call and UTF-8 byte measurements from checked-in canonical routes; these are evidence, not product-wide limits. Byte accounting covers each exact camelCase MCP tool arguments object and snake_case structured response only; it excludes MCP framing, logs, viewer traffic, and token estimates. Bounded straight-line burst fixtures choose an observation-derived bound from 1 through 16, never exceed the paired step route's action-call count, and reduce calls for at least six of eight lessons. Checked-in non-practice seeds are deterministic evaluator evidence, not qualification-time decks.",
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
            f"A successfully finalized completed, capped, or explicitly finished match returns `{result_schema}` with final state hash, replay payload hash, rules and mode identity, outcome, metrics, and verification code. A styled result carries exactly two criterion outcomes independently reconstructed from and bound to that verified replay. A Signal School result carries ordered requirement outcomes, a factual review, the replay payload hash, a distinct bounded attempt-evidence hash, and their aggregate evidence hash. Failed-closed finalization returns neither a verified result, a style or lesson outcome, nor a verified replay.",
            "",
            "# Persistence",
            "",
            "Replay saving is an explicit call into the bounded application-owned replay store. The agent supplies no path. The saved file is reloaded and verified before the existing replay presentation consumes it. Replay schema 1 stores accepted rules steps only; the bounded Signal School attempt witnesses remain ephemeral host result evidence until a future exhibition receipt explicitly persists both evidence domains.",
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
