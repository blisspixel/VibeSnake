using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [McpServerResource(
        UriTemplate = "vibesnake://agent/rules",
        Name = "Vibe Snake agent rules",
        MimeType = "application/json")]
    [Description("Closed action, timing, observation, replay, and privacy rules for external-agent matches.")]
    public static string GetRules() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-rules-resource-v5",
            ruleset_id = RulesetIdentity.CurrentId,
            rules_version = RulesetIdentity.CurrentVersion,
            observation_schema = AgentObservationV3.Contract,
            observation_profile = AgentPassportV2.SymbolicStepObservationProfile,
            passport_schema = AgentPassportV2.Contract,
            identity_resource = "vibesnake://agent/identity",
            actions = Enum.GetNames<AgentAction>().Select(value => value.ToLowerInvariant()),
            action_profiles = new[]
            {
                AgentPassportV2.FourDirectionActionProfile,
                AgentPassportV2.FourDirectionBurstActionProfile,
            },
            public_intents = Enum.GetNames<AgentPublicIntent>()
                .Select(value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value)),
            action_semantics = "play_move advances exactly one clock-free rules step in four-direction-step-v1. play_burst advances at most 16 steps in four-direction-burst-v1, applying one initial action and then continuing until its bound or a fixed public decision event. Rejected mutations advance none.",
            burst = new
            {
                contract = AgentBurstPolicy.Contract,
                maximum_steps = AgentBurstRequest.MaximumBurstSteps,
                stop_events = AgentBurstPolicy.Stops.Select(value =>
                    JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString())),
                fixed_continuation = true,
                viewer_frames_per_burst = 1,
            },
            viewer = new
            {
                frame_contract = AgentViewerFrameV5.Contract,
                operations = Enum.GetNames<AgentViewerOperationKind>()
                    .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName),
                pre_mutation_tick_and_state_hash = true,
                exact_steps_advanced = true,
                burst_stop_reason_and_event = true,
                monotonic_sequence = true,
                delivery = "The host retains only the newest unsent frame. Consumers report sequence gaps as coalesced earlier updates; the verified replay remains canonical.",
                awaiting_agent = "Awaiting an agent action pauses rules and score while the viewer remains presentation-only.",
            },
            intent_semantics = "A public intent is an optional self-declared presentation label. It never changes rules, score, rewards, replay verification, or qualification.",
            stale_action_guard = StaleActionGuards,
            seed_divisions = SeedDivisions,
            maximum_steps = AgentMatchOptions.MaximumAllowedSteps,
            maximum_unique_mutations_per_match = AgentMatchSession.MaximumUniqueMutations,
            maximum_retained_matches = AgentSessionRegistry.MaximumRetainedMatches,
            live_match_idle_lease_minutes = AgentSessionRegistry.LiveMatchIdleLeaseMinutes,
            idle_reclamation = "At capacity, only an inactive live match whose 30-minute valid-handle operation lease expired may be reclaimed. Reclamation creates no result or replay, and viewer activity never refreshes or ends the lease.",
            replay = "A successfully finalized completed, capped, or explicitly finished match returns a deterministic verified lane result and replay. Failed-closed finalization returns neither; an exhibition receipt is not part of this contract.",
            rivalry = "An optional built-in rival advances once per accepted agent step on the same seed and exact configuration. Each lane has an independent verified replay.",
            privacy = "Observations exclude random state, future outcomes, controller internals, profiles, progression, paths, prompts, credentials, diagnostics, and hidden reasoning.",
        },
        JsonOptions);

    [McpServerResource(
        UriTemplate = "vibesnake://agent/identity",
        Name = "Vibe Snake agent identity catalog",
        MimeType = "application/json")]
    [Description("Closed presentation identities accepted by Agent Passport v2.")]
    public static string GetIdentity() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-identity-resource-v1",
            passport_schema = AgentPassportV2.Contract,
            observation_profile = AgentPassportV2.SymbolicStepObservationProfile,
            avatars = CosmeticSetCatalog.Sets.Select(avatar => new
            {
                id = avatar.Id,
                name = avatar.Name,
            }),
            accents = AgentAccentCatalog.All.Select(accent => new
            {
                id = accent.Id,
                name = accent.DisplayName,
                color = new
                {
                    red = accent.Color.Red,
                    green = accent.Color.Green,
                    blue = accent.Color.Blue,
                },
            }),
            stations = StationIdentityCatalog.All.Select(station => new
            {
                id = station.Id,
                name = station.DisplayName,
            }),
            semantics = new
            {
                declaration = "Passport identity is caller-declared and catalog-validated, not authenticated.",
                presentation = "Avatar, accent, and station choices are presentation-only and never change rules, score, verification, or qualification.",
                independence = "Agent presentation is independent of the watching human's selected cosmetic and progression unlocks.",
                station_boundary = "A station identity is a presentation affinity, not approval to schedule, publish, moderate, or provide station audio.",
            },
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
            contract = "vibesnake-agent-signal-school-v2",
            start_tool = "start_lesson",
            practice_semantics = "Each lesson owns its canonical open seed, mode, step cap, and primary public metric target. Target reached is practice evidence, not mastery or qualification.",
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

        1. Read `vibesnake://agent/rules`, `vibesnake://agent/modes`, and `vibesnake://agent/identity`. Optionally read the style, rival, or Signal School resources.
        2. Call `start_match` for an exhibition, or `start_lesson` with a closed Signal School lesson ID for canonical open-seed practice. Select either `four-direction-step-v1` or `four-direction-burst-v1`. If supplying a passport, use only the closed avatar, accent, and station IDs from the identity resource.
        3. Read the returned observation. Use only visible board state.
        4. Use `play_move` for one exact decision. In a burst-profile match, use `play_burst` for a safe straight continuation of at most 16 steps; it stops on the first fixed public decision event. Supply the exact tick and state hash plus a new idempotency key. Optionally declare one closed public intent so a viewer can follow the plan.
        5. On rejection or burst stop, inspect the reason, final-step public events, and refreshed observation. Rejected requests do not step the rules.
        6. Continue until the result appears, or call `finish_match` to request early finalization.
        7. Confirm that finalization returned a verified result. Call `save_verified_replay` only when persistence for later human viewing is desired.

        Public intents are `seek_food`, `seek_power`, `preserve_space`, `take_risk`, and `recover`. They are self-reported presentation only. `continue` preserves the current direction. Never submit the current direction or its opposite as a turn. Response latency has no scoring effect. At capacity, a live handle idle for 30 minutes may be reclaimed without producing a result or replay; viewer activity is never match control.
        """;
}
