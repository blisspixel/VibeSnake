using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.AgentHost;

[McpServerResourceType]
public sealed class AgentResources
{
    private static readonly string[] StaleActionGuards =
        ["expected_tick", "expected_state_hash", "idempotency_key"];
    private static readonly string[] SeedDivisions = ["open", "blind"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    [McpServerResource(
        UriTemplate = "vibesnake://agent/rules",
        Name = "Vibe Snake agent rules",
        MimeType = "application/json")]
    [Description("Closed action, timing, observation, replay, and privacy rules for external-agent matches.")]
    public static string GetRules() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-rules-resource-v1",
            ruleset_id = RulesetIdentity.CurrentId,
            rules_version = RulesetIdentity.CurrentVersion,
            observation_schema = AgentObservationV1.Contract,
            actions = Enum.GetNames<AgentAction>().Select(value => value.ToLowerInvariant()),
            public_intents = Enum.GetNames<AgentPublicIntent>()
                .Select(value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value)),
            action_semantics = "An accepted action advances exactly one clock-free rules step. A rejected action advances none.",
            intent_semantics = "A public intent is an optional self-declared presentation label. It never changes rules, score, rewards, replay verification, or qualification.",
            stale_action_guard = StaleActionGuards,
            seed_divisions = SeedDivisions,
            maximum_steps = AgentMatchOptions.MaximumAllowedSteps,
            replay = "A successfully finalized completed, capped, or explicitly finished match returns a deterministic verified lane result and replay. Failed-closed finalization returns neither; an exhibition receipt is not part of this contract.",
            rivalry = "An optional built-in rival advances once per accepted agent step on the same seed and exact configuration. Each lane has an independent verified replay.",
            privacy = "Observations exclude random state, future outcomes, controller internals, profiles, progression, paths, prompts, credentials, diagnostics, and hidden reasoning.",
        },
        JsonOptions);

    [McpServerResource(
        UriTemplate = "vibesnake://agent/modes",
        Name = "Vibe Snake official modes",
        MimeType = "application/json")]
    [Description("The only run modes and fixed configurations accepted by start_match.")]
    public static string GetModes() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-modes-resource-v1",
            modes = RunModeCatalog.All.Select(mode => new
            {
                id = mode.Id,
                version = mode.Version,
                name = mode.DisplayName,
                description = mode.Description,
                board_width = mode.BoardWidth,
                board_height = mode.BoardHeight,
                adaptive_state = mode.AdaptiveState.ToString(),
                adaptive_policy_id = mode.AdaptivePolicyId,
            }),
        },
        JsonOptions);

    [McpServerResource(
        UriTemplate = "vibesnake://agent/styles",
        Name = "Vibe Snake style contracts",
        MimeType = "application/json")]
    [Description("Agent-selectable play styles with public metrics, targets, and supported modes.")]
    public static string GetStyles() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-style-catalog-v1",
            styles = AgentStyleContractCatalog.All,
        },
        JsonOptions);

    [McpServerResource(
        UriTemplate = "vibesnake://agent/signal-school",
        Name = "Vibe Snake Signal School",
        MimeType = "application/json")]
    [Description("Deterministic practice lessons for learning the public observation and action loop.")]
    public static string GetSignalSchool() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-signal-school-v1",
            lessons = AgentSignalSchoolCatalog.All,
        },
        JsonOptions);

    [McpServerResource(
        UriTemplate = "vibesnake://agent/rivals",
        Name = "Vibe Snake built-in rivals",
        MimeType = "application/json")]
    [Description("The named, deterministic built-in personalities available for equal-seed agent exhibitions.")]
    public static string GetRivals() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-rival-catalog-v1",
            rivals = AiPersonalityCatalog.BuiltIn.Select(personality => new
            {
                id = personality.Id,
                name = personality.Name,
                description = personality.Description,
                controller = AiPersonalityController.AlgorithmId,
            }),
        },
        JsonOptions);

    [McpServerResource(
        UriTemplate = "vibesnake://agent/playbook",
        Name = "Vibe Snake agent playbook",
        MimeType = "text/markdown")]
    [Description("Compact, transport-independent instructions for completing an agent match safely.")]
    public static string GetPlaybook() =>
        """
        # Vibe Snake Agent Playbook

        1. Read `vibesnake://agent/rules`, `vibesnake://agent/modes`, and optionally the style, rival, or Signal School resources.
        2. Call `start_match` with `classic` or `vibe` and an open or blind seed division.
        3. Read the returned observation. Use only visible board state.
        4. Call `play_move` with the exact tick and state hash plus a new idempotency key. Optionally declare one closed public intent so a viewer can follow the plan.
        5. On rejection, inspect the rejection and refreshed observation. Rejected requests do not step the rules.
        6. Continue until the result appears, or call `finish_match` to request early finalization.
        7. Confirm that finalization returned a verified result. Call `save_verified_replay` only when persistence for later human viewing is desired.

        Public intents are `seek_food`, `seek_power`, `preserve_space`, `take_risk`, and `recover`. They are self-reported presentation only. `continue` preserves the current direction for one step. Never submit the current direction or its opposite as a turn. Response latency has no scoring effect.
        """;
}
