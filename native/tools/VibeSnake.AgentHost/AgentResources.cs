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
    private static readonly string[] InteractionAccountingExclusions =
    [
        "mcp_or_json_rpc_framing",
        "logs_or_stderr",
        "viewer_traffic",
        "token_estimates",
    ];
    private static readonly string[] QualificationEvidenceIncludedCalls =
        ["start_lesson", "play_move", "play_burst", "finish_match"];
    private static readonly string[] QualificationEvidenceDimensions =
        ["lesson_id", "action_profile"];
    private static readonly string[] QualificationEvidenceMeasures =
    [
        "action_calls",
        "request_utf8_bytes",
        "response_utf8_bytes",
        "total_utf8_bytes",
    ];
    private static readonly QualificationEvidenceObservation[] QualificationEvidenceObservations =
    [
        new("first-turn", AgentPassportV4.FourDirectionActionProfile, 2, 411, 12_765, 13_176),
        new("first-turn", AgentPassportV4.FourDirectionBurstActionProfile, 2, 462, 12_905, 13_367),
        new("wrap-line", AgentPassportV4.FourDirectionActionProfile, 32, 5_012, 116_979, 121_991),
        new("wrap-line", AgentPassportV4.FourDirectionBurstActionProfile, 2, 460, 12_757, 13_217),
        new("hunger-route", AgentPassportV4.FourDirectionActionProfile, 22, 3_600, 83_092, 86_692),
        new("hunger-route", AgentPassportV4.FourDirectionBurstActionProfile, 3, 649, 16_630, 17_279),
        new("exit-route", AgentPassportV4.FourDirectionActionProfile, 9, 1_478, 38_014, 39_492),
        new("exit-route", AgentPassportV4.FourDirectionBurstActionProfile, 9, 1_695, 38_656, 40_351),
        new("power-route", AgentPassportV4.FourDirectionActionProfile, 316, 50_378, 1_148_680, 1_199_058),
        new("power-route", AgentPassportV4.FourDirectionBurstActionProfile, 106, 19_440, 402_803, 422_243),
        new("recover-route", AgentPassportV4.FourDirectionActionProfile, 346, 56_518, 1_300_457, 1_356_975),
        new("recover-route", AgentPassportV4.FourDirectionBurstActionProfile, 116, 21_685, 452_163, 473_848),
        new("combo-route", AgentPassportV4.FourDirectionActionProfile, 80, 12_706, 289_594, 302_300),
        new("combo-route", AgentPassportV4.FourDirectionBurstActionProfile, 40, 7_359, 151_327, 158_686),
        new("death-read", AgentPassportV4.FourDirectionActionProfile, 139, 21_810, 504_930, 526_740),
        new("death-read", AgentPassportV4.FourDirectionBurstActionProfile, 89, 16_033, 338_959, 354_992),
    ];
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
            contract = "vibesnake-agent-rules-resource-v7",
            ruleset_id = RulesetIdentity.CurrentId,
            rules_version = RulesetIdentity.CurrentVersion,
            observation_schema = AgentObservationV5.Contract,
            result_schema = AgentMatchResultV5.Contract,
            observation_profile = AgentPassportV4.SymbolicStepObservationProfile,
            passport_schema = AgentPassportV4.Contract,
            identity_resource = "vibesnake://agent/identity",
            actions = Enum.GetNames<AgentAction>().Select(value => value.ToLowerInvariant()),
            action_profiles = new[]
            {
                AgentPassportV4.FourDirectionActionProfile,
                AgentPassportV4.FourDirectionBurstActionProfile,
            },
            public_intents = Enum.GetNames<AgentPublicIntent>()
                .Select(value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value)),
            action_semantics = "play_move advances exactly one clock-free rules step in four-direction-step-v1. play_burst advances at most 16 steps in four-direction-burst-v1, applying one initial action and then continuing until its bound or a fixed public decision event. Preflight and logical rejections advance none; a post-step replay failure may report rules_advanced=true and always fails closed.",
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
                frame_contract = AgentViewerFrameV7.Contract,
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
            lesson_evidence = "Accepted-step lesson facts are independently reconstructed from replay schema 1. Rejection-aware facts use a separate bounded canonical attempt-witness sequence. A verified lesson outcome binds the replay payload hash and attempt-evidence hash into one evidence hash; the ordinary saved replay does not contain the attempt witnesses.",
            replay = "A successfully finalized rules-terminal, capped, lesson-complete, or explicitly finished match returns a deterministic verified lane result and replay. finish_match reports completed only after all lesson requirements are satisfied; other nonterminal early finishes report aborted. Style criteria are measurements against optional targets, not match grades. Failed-closed finalization returns no verified result; an exhibition receipt is not part of this contract.",
            rivalry = "An optional built-in rival advances once per accepted agent step on the same seed and exact configuration. Each lane has an independent verified replay.",
            privacy = "Observations exclude random state, future outcomes, controller internals, profiles, progression, paths, prompts, credentials, diagnostics, and hidden reasoning.",
        },
        JsonOptions);

    [McpServerResource(
        UriTemplate = "vibesnake://agent/identity",
        Name = "Vibe Snake agent identity catalog",
        MimeType = "application/json")]
    [Description("Closed presentation identities accepted by Agent Passport v4.")]
    public static string GetIdentity() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-identity-resource-v3",
            passport_schema = AgentPassportV4.Contract,
            observation_profile = AgentPassportV4.SymbolicStepObservationProfile,
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
    [Description("Agent-selectable play styles with two closed replay-derived criteria and supported modes.")]
    public static string GetStyles() => JsonSerializer.Serialize(
        new
        {
            contract = "vibesnake-agent-style-catalog-v2",
            progress_schema = AgentStyleProgressV2.Contract,
            outcome_schema = AgentStyleOutcomeV2.Contract,
            semantics = new
            {
                live = "Observation values are rules-advanced-step facts and may rise or fall. They are not replay-verified outcomes.",
                terminal = "A successfully finalized styled result contains a style outcome reconstructed from the verified replay and bound to its payload hash.",
                interpretation = "Criteria measure observed behavior against optional style targets. Satisfaction is not a pass/fail grade for the match and does not prove intent, planning, mastery, personality, or spectator appeal.",
            },
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
            contract = "vibesnake-agent-signal-school-v3",
            start_tool = "start_lesson",
            evaluation_policy = AgentSignalSchoolCatalog.EvaluationPolicyId,
            maximum_attempt_witnesses = AgentSignalSchoolCatalog.MaximumAttemptWitnesses,
            progress_schema = AgentLessonProgressV2.Contract,
            delta_schema = AgentLessonProgressDeltaV2.Contract,
            outcome_schema = AgentLessonOutcomeV2.Contract,
            retry_schema = AgentLessonRetryDescriptorV1.Contract,
            practice_semantics = "Each lesson owns its canonical open seed, mode, step cap, and ordered closed requirements. All requirements satisfied is practice evidence, not mastery or qualification.",
            evidence_semantics = new
            {
                live = "Observation progress is live evidence and names the first unmet ordered requirement.",
                replay = "Accepted-step requirements are independently reconstructed from the verified replay.",
                attempts = "Rejection-aware requirements use a bounded canonical attempt-witness sequence distinct from replay schema 1. Exact retries do not add evidence; stale, conflicting, capacity, or wrong-profile requests cannot qualify.",
                terminal = "A verified outcome binds the replay payload hash and distinct attempt-evidence hash into one evidence hash and reports a closed factual review code.",
                failed_closed = "Replay failure creates no verified lesson outcome. Retry guidance always starts the same canonical lesson in a fresh session without inherited state, handle, mutation keys, or practice history.",
            },
            interaction_accounting = new
            {
                policy = "mcp-tool-arguments-and-structured-response-json-v1",
                action_calls = "Count only play_move and play_burst calls.",
                request_utf8_bytes = "Compact JSON for each exact MCP tool arguments object, using the discovered camelCase parameter names, encoded as UTF-8.",
                response_utf8_bytes = "Compact snake_case structured response JSON encoded as UTF-8.",
                excluded = InteractionAccountingExclusions,
            },
            qualification_evidence = new
            {
                status = "measured",
                unit = "one canonical route for one lesson and action profile",
                included_calls = QualificationEvidenceIncludedCalls,
                request_policy = "Fields use the actual discovered camelCase MCP tool parameter names and order. start_lesson explicitly supplies lessonId and actionProfile. Play calls supply only required arguments. finish_match is always measured. Optional watch, passport, and declaredIntent arguments are omitted.",
                fixture_policy = "Each deterministic fixture uses match_route-{lesson_id}; mutation keys are route-{lesson_id}-reversal when required and route-{lesson_id}-{zero_based_step}. Lesson mode, seed, and step cap come from the published definition.",
                burst_measurement = "four-direction-burst-v1 uses an observation-derived maximumSteps between 1 and 16 for each measured bounded straight-line play_burst call; normal public decision-event stops still apply.",
                regression_policy = "Each paired burst route must use no more action calls than its step route, at least six of eight lessons must use fewer, and every exact observation change requires review.",
                dimensions = QualificationEvidenceDimensions,
                measures = QualificationEvidenceMeasures,
                observations = QualificationEvidenceObservations,
            },
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
        2. Call `start_match` for an exhibition, or `start_lesson` with a closed Signal School lesson ID for canonical open-seed practice. Select either `four-direction-step-v1` or `four-direction-burst-v1`. If supplying Passport v4, use only the closed avatar, accent, and station IDs from the identity resource and declare `symbolic-step-v4`.
        3. Read the returned observation. Use only visible board state.
        4. Use `play_move` for one exact decision. In a burst-profile match, use `play_burst` for a safe straight continuation of at most 16 steps; it stops on the first fixed public decision event. Supply the exact tick and state hash plus a new idempotency key. Optionally declare one closed public intent so a viewer can follow the plan.
        5. On rejection or burst stop, inspect the reason, actual advancement, final-step public events, and refreshed observation. Preflight and logical rejections do not step the rules. A `replay_failure` can report `rules_advanced=true` after a real step and always fails closed without a verified result.
        6. Continue until the result appears. After all Signal School requirements are satisfied, call `finish_match` to finalize a completed lesson. In any other running match, `finish_match` requests an aborted early finish.
        7. Confirm that finalization returned a verified result. For a styled match, use only its replay-bound style outcome as verified criterion evidence. For Signal School, use the verified lesson outcome, which binds replay-trace and any distinct attempt-witness evidence; a failed-closed session has no verified lesson outcome and must be retried through a fresh `start_lesson`. Call `save_verified_replay` only when accepted-step replay persistence for later human viewing is desired.

        Public intents are `seek_food`, `seek_power`, `preserve_space`, `take_risk`, and `recover`. They are self-reported presentation only. `continue` preserves the current direction. Never submit the current direction or its opposite as a turn. Response latency has no scoring effect. At capacity, a live handle idle for 30 minutes may be reclaimed without producing a result or replay; viewer activity is never match control.
        """;

    private sealed record QualificationEvidenceObservation(
        string LessonId,
        string ActionProfile,
        int ActionCalls,
        int RequestUtf8Bytes,
        int ResponseUtf8Bytes,
        int TotalUtf8Bytes);
}
