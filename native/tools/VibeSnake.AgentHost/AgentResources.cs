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
    private static readonly string[] ArchiveTools =
        ["archive_exhibition", "list_exhibitions", "forget_exhibition"];
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
        new("first-turn", AgentPassportV4.FourDirectionActionProfile, 2, 411, 12_549, 12_960),
        new("first-turn", AgentPassportV4.FourDirectionBurstActionProfile, 2, 462, 12_688, 13_150),
        new("wrap-line", AgentPassportV4.FourDirectionActionProfile, 32, 5_012, 116_044, 121_056),
        new("wrap-line", AgentPassportV4.FourDirectionBurstActionProfile, 2, 460, 12_541, 13_001),
        new("hunger-route", AgentPassportV4.FourDirectionActionProfile, 22, 3_600, 82_394, 85_994),
        new("hunger-route", AgentPassportV4.FourDirectionBurstActionProfile, 3, 649, 16_387, 17_036),
        new("exit-route", AgentPassportV4.FourDirectionActionProfile, 9, 1_478, 37_630, 39_108),
        new("exit-route", AgentPassportV4.FourDirectionBurstActionProfile, 9, 1_695, 38_271, 39_966),
        new("power-route", AgentPassportV4.FourDirectionActionProfile, 316, 50_378, 1_140_927, 1_191_305),
        new("power-route", AgentPassportV4.FourDirectionBurstActionProfile, 106, 19_440, 400_089, 419_529),
        new("recover-route", AgentPassportV4.FourDirectionActionProfile, 346, 56_518, 1_291_982, 1_348_500),
        new("recover-route", AgentPassportV4.FourDirectionBurstActionProfile, 116, 21_685, 449_207, 470_892),
        new("combo-route", AgentPassportV4.FourDirectionActionProfile, 80, 12_706, 287_505, 300_211),
        new("combo-route", AgentPassportV4.FourDirectionBurstActionProfile, 40, 7_359, 150_197, 157_556),
        new("death-read", AgentPassportV4.FourDirectionActionProfile, 139, 21_810, 520_505, 542_315),
        new("death-read", AgentPassportV4.FourDirectionBurstActionProfile, 89, 16_033, 348_781, 364_814),
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
            contract = "vibesnake-agent-rules-resource-v15",
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
                frame_contract = AgentViewerFrameV9.Contract,
                operations = Enum.GetNames<AgentViewerOperationKind>()
                    .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName),
                pre_mutation_tick_and_state_hash = true,
                exact_steps_advanced = true,
                burst_stop_reason_and_event = true,
                monotonic_sequence = true,
                delivery = "The host retains only the newest unsent frame. Consumers report sequence gaps as coalesced earlier updates; the verified replay remains canonical.",
                awaiting_agent = "Awaiting an agent action pauses rules and score while the viewer remains presentation-only.",
                survival_state = new
                {
                    contract = AgentSurvivalStateV1.Contract,
                    candidate_exits = AgentSurvivalStateV1.RunningCandidateExits,
                    exit_pressure = Enum.GetNames<AgentExitPressureV1>()
                        .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName),
                    recovery_resources = AgentSurvivalStateV1.RecoveryOrder
                        .Select(value =>
                            JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString())),
                    definition = "structural_open_exits counts the non-reversal directions whose next cell is not occupied by the body or a detached obstacle, using the departing-tail rule. It is the same structural-exit definition the Stillwater style criterion measures.",
                    derivation = "Every survival field is derived from public board state the same frame already carries. A viewer recomputes all of it from the observation and rejects a frame that disagrees with itself.",
                    boundary = "exit_pressure is a threshold crossing of structural_open_exits, not a grade, a prediction, or advice. The block never names a direction to take, and an agent that wants a route must still compute one.",
                    scope = "This block is spectator presentation on the viewer frame. The agent observation is unchanged, because an agent can already derive every one of these facts from the board it receives.",
                },
            },
            intent_semantics = "A public intent is an optional self-declared presentation label. It never changes rules, score, rewards, replay verification, or qualification.",
            lifecycle_semantics = new
            {
                lifecycle = "lifecycle describes the agent session: awaiting_action, completed, aborted, or failed_closed. It never describes the snake.",
                run_status = "run_status describes the snake inside the rules: running, dead, or won. A completed lesson can report run_status running because the agent deliberately stopped a living run.",
                pairing = "lifecycle completed with run_status running is the normal, correct result of finishing a satisfied lesson early. lifecycle and run_status answer different questions and are never merged.",
                is_action_awaited = "is_action_awaited stays true while the host would still accept a mutation, including after every lesson requirement is satisfied. Satisfying requirements never ends a match; only finish_match, a rules terminal, or the step cap does.",
                requirement_satisfied = "A lesson requirement's satisfied flag reports that its closed evidence exists. It is a factual observation, not a grade, score, or claim about mastery.",
                recommended_next_tool = "recommended_next_tool is factual guidance derived from live progress. The caller keeps explicit control and may keep playing instead.",
            },
            argument_binding = "Tool arguments use the exact discovered camelCase names and JSON types. Missing, unexpected, and wrong-typed argument names are named before the tool runs, list the required and optional fields, and change no match state. gameplaySeed is a quoted decimal string, never a JSON number. A rejected request carries no observation because it never entered match code; observe_match separately proves the unchanged tick and state hash.",
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
            receipt = new
            {
                contract = AgentExhibitionReceiptV2.Contract,
                status_contract = AgentExhibitionReceiptStatusV1.Contract,
                division_contract = AgentDivisionIdentityV1.Contract,
                tool = "get_exhibition_receipt",
                availability = "A receipt exists only for a successfully finalized, verified match. A live, unverified, or failed-closed match returns is_available false. A rivalry is receipted only when both lanes verified independently.",
                canonical_hash = "receipt_hash names this exhibition instance. It covers the match identity, division, passport, lifecycle, end reason, seed, terminal facts, both verified lane replay hashes, the replay-derived style and lesson evidence hashes, and every accepted presentation event. Because it binds the match handle, a rematch of the same line always mints a new receipt_hash.",
                route_identity_hash = "route_identity_hash names the line rather than the visit. It covers the division, seed, terminal facts, both verified lane replay hashes, and the style and lesson satisfaction outcome, and it deliberately omits the match handle, the caller-declared passport, presentation events, and any attempt evidence derived from idempotency keys. The same seed and route reproduce it across separate matches and separate host processes, so use it to recognise an already-walked line and to compare same-seed rematches.",
                display_time = "display_time_utc is presentation-only, may be absent, and is deliberately excluded from receipt_hash so the same exhibition keeps one identity whenever it is shown.",
                presentation_events = "accepted_presentation_events record the ordered tick, action, and self-declared public intent of each accepted rules step. They are spectator labels and never changed rules, score, or verification.",
                boundary = "The receipt is transport-neutral and local. Persisted passports and league standings remain separate future work.",
            },
            archive = new
            {
                contract = AgentExhibitionArchiveV2.Contract,
                entry_contract = AgentArchivedExhibitionV2.Contract,
                status_contract = AgentExhibitionArchiveStatusV2.Contract,
                listing_contract = AgentExhibitionArchiveListingV1.Contract,
                forget_contract = AgentExhibitionForgetStatusV1.Contract,
                index_contract = AgentExhibitionArchiveIndexV3.Contract,
                index_entry_contract = AgentArchivedExhibitionIndexEntryV3.Contract,
                drop_contract = AgentExhibitionArchiveDropV1.Contract,
                tools = ArchiveTools,
                schema_version = AgentExhibitionArchiveV2.CurrentSchemaVersion,
                migrates_from_schema_version = AgentExhibitionArchiveV2.LegacySchemaVersion,
                capacity = AgentExhibitionArchiveV2.MaximumEntries,
                maximum_bytes = AgentExhibitionArchiveV2.MaximumBytes,
                archive_codes = Enum.GetNames<AgentExhibitionArchiveCode>()
                    .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)
                    .ToArray(),
                forget_codes = Enum.GetNames<AgentExhibitionForgetCode>()
                    .Select(JsonNamingPolicy.SnakeCaseLower.ConvertName)
                    .ToArray(),
                definition = "An archive entry keeps one verified exhibition: its canonical receipt verbatim, plus the saved replay file name of every lane the receipt contains. It is the durable half of the exhibition loop, and it exists so a person can find a match again after the host process that played it has exited.",
                prerequisites = "Archiving is explicit and ordered. A match must be finalized and verified so it has a receipt, and save_verified_replay must already have written every lane, because an archived exhibition names files rather than hopes. A rivalry archives both lanes or neither.",
                boundary = "The archive is local, bounded, and lives outside the supported player Persistence assembly. It stores no human score, progression, achievement, cosmetic, or profile data, and it never affects them. It accepts no path. forget_exhibition removes archive entries only and never touches a saved replay file or any other store.",
                capacity_rule = "Effective capacity is the lesser of the entry and byte bounds. A receipt carries one accepted presentation event per accepted rules step, so a long exhibition is much larger than a short one and the byte ceiling can evict before 32 entries are reached. Every archive response therefore publishes bytes_used, maximum_bytes, remaining_entries, and remaining_bytes rather than an entry count alone.",
                durability = "The write is atomic: a complete document is staged and then replaced, so an interrupted write leaves the previous archive intact. At capacity the oldest exhibitions are evicted first and every dropped exhibition is named by receipt and route hash rather than counted. Archiving the same exhibition again writes nothing and reports already_archived, so the call is safe to repeat.",
                integrity = "Every stored entry must recompute both of its canonical receipt hashes and agree with each promoted field copied from that receipt. A document that fails is quarantined beside the archive rather than repaired, and the caller is told through recovered_from_corruption. If it can be neither read nor moved aside, the write is refused as archive_unavailable rather than overwriting evidence. A different exhibition is never written under an existing receipt hash.",
                migration = "A schema-1 archive written by an earlier host is migrated forward on read rather than quarantined, and migrated_from_legacy_schema reports it. Migration is lossless by construction because every field the current schema promotes is derived from the receipt the older schema already stored verbatim, and every rebuilt entry must verify against that receipt exactly as a freshly archived one would.",
                listing = "list_exhibitions reads the archive without writing to it and optionally narrows to one route_identity_hash, which the same division, seed, and verified replays reproduce across matches and host processes. Each listed entry reports whether its named lane replay files are still present, because an entry names a file rather than embedding it and that file can be deleted after archiving.",
                accounting = "bytes_used is what the archive file holds right now and is verifiable against it. bytes_projected is what the next write would produce and is the size the byte ceiling binds, so remaining_bytes follows it. The two differ only between a migrate-on-read and the next write, because reading never writes. schema_version is the document in memory and stored_schema_version is the one on disk, so a pending migration reads as 1 to 2 rather than as a bare boolean.",
                ordering = "Entries are oldest first and eviction takes position 0. Every listed entry carries its position in the whole store rather than in the listing, so a filtered listing still says where an exhibition sits and which one eviction reaches next. Position is computed at read time and is not stored, because order is a property of the store rather than of any one exhibition.",
                identity = "Presentation display time is stripped before an exhibition is stored, because display time is never part of exhibition identity and an archive that kept it would make one exhibition look different on every visit.",
            },
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
            contract = "vibesnake-agent-style-catalog-v3",
            progress_schema = AgentStyleProgressV3.Contract,
            outcome_schema = AgentStyleOutcomeV3.Contract,
            semantics = new
            {
                live = "Observation values are rules-advanced-step facts and may rise or fall. They are not replay-verified outcomes.",
                terminal = "A successfully finalized styled result contains a style outcome reconstructed from the verified replay and bound to its payload hash.",
                interpretation = "ThresholdReached, ThresholdsReached, and AllThresholdsReached report measurement threshold crossings only. They are not pass/fail grades for the match and do not prove intent, planning, mastery, personality, or spectator appeal.",
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
            contract = "vibesnake-agent-signal-school-v4",
            start_tool = "start_lesson",
            evaluation_policy = AgentSignalSchoolCatalog.EvaluationPolicyId,
            maximum_attempt_witnesses = AgentSignalSchoolCatalog.MaximumAttemptWitnesses,
            progress_schema = AgentLessonProgressV3.Contract,
            delta_schema = AgentLessonProgressDeltaV2.Contract,
            outcome_schema = AgentLessonOutcomeV3.Contract,
            retry_schema = AgentLessonRetryDescriptorV1.Contract,
            practice_semantics = "Each lesson owns its canonical open seed, mode, step cap, and ordered closed requirements. requirements_satisfied is the integer count of satisfied entries in requirements. All requirements satisfied is practice evidence, not mastery or qualification. A live completed lesson reports recommended_next_tool as finish_match.",
            evidence_semantics = new
            {
                live = "Observation progress is live evidence and names the first unmet ordered requirement.",
                replay = "Accepted-step requirements are independently reconstructed from the verified replay.",
                attempts = "Rejection-aware requirements use a bounded canonical attempt-witness sequence distinct from replay schema 1. Exact retries do not add evidence; stale, conflicting, capacity, or wrong-profile requests cannot qualify.",
                terminal = "A verified outcome binds the replay payload hash and distinct attempt-evidence hash into one evidence hash and reports a closed factual review code. A reached target omits retry guidance; an unmet outcome includes it.",
                failed_closed = "Replay failure creates no verified lesson outcome. Retry guidance starts the same canonical lesson in a fresh session without inherited state, handle, mutation keys, or practice history.",
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
        6. Continue until the result appears. When Signal School reports `recommended_next_tool: finish_match`, call it to finalize a completed lesson without padding steps. In any other running match, `finish_match` requests an aborted early finish.
        7. Confirm that finalization returned a verified result. A reached lesson target omits retry guidance; only an unmet outcome or failed-closed progress provides a fresh `start_lesson` descriptor. For a styled match, threshold flags are measurement crossings, not grades, and only its replay-bound style outcome is verified criterion evidence. Call `save_verified_replay` only when accepted-step replay persistence for later human viewing is desired.

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
