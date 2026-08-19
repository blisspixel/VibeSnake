using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using VibeSnake.AgentPlay;

namespace VibeSnake.AgentHost;

[McpServerToolType]
public sealed class McpAgentTools
{
    private readonly AgentSessionRegistry _registry;

    public McpAgentTools(AgentSessionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    [McpServerTool(
        Name = "start_match",
        Title = "Start Vibe Snake match",
        UseStructuredContent = true,
        OutputSchemaType = typeof(StartAgentMatchV5),
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Starts one isolated, clock-free Vibe Snake agent match and returns its explicit opaque handle plus initial public observation. Use only classic or vibe. Blind matches reject caller-selected seeds. Use the exact discovered camelCase arguments and JSON types; gameplaySeed is a quoted decimal string such as \"42\", never a JSON number. Missing, unexpected, or wrong-typed argument names are rejected before this tool runs with the exact mismatch and no state change.")]
    public StartAgentMatchV5 StartMatch(
        [Description("Official mode ID: classic or vibe.")] string modeId,
        [Description("Seed division: open or blind.")] AgentSeedVisibility seedVisibility,
        [Description("Optional unsigned 64-bit seed for open matches, supplied as a quoted decimal string such as \"42\". Use null to let the host generate one. Must be null for blind matches.")] string? gameplaySeed = null,
        [Description("Optional rules-step cap from 1 through 2000. Use null for 2000.")] int? maximumSteps = null,
        [Description("Optional style contract: stillwater, crownchaser, edge-prophet, mutagenist, or redline. Mode restrictions are enforced.")] string? styleContractId = null,
        [Description("Optional built-in rival personality ID. Both lanes use the same seed and exact rules configuration.")] string? rivalPersonalityId = null,
        [Description("Set true to mint a one-time same-user named-pipe capability for a read-only local viewer.")] bool watchEnabled = false,
        [Description("Optional public Agent Passport v4. Avatar, accent, and station IDs must come from vibesnake://agent/identity; the display name is presentation-only; and its observation and action profiles must match the host contract.")] AgentPassportV4? passport = null,
        [Description("Control division: four-direction-step-v1 or four-direction-burst-v1. The default preserves one-step play.")] string actionProfile = AgentPassportV4.FourDirectionActionProfile) =>
        Execute(() => _registry.StartMatch(
            modeId,
            seedVisibility,
            gameplaySeed,
            maximumSteps,
            styleContractId,
            rivalPersonalityId,
            watchEnabled,
            passport,
            actionProfile));

    [McpServerTool(
        Name = "start_lesson",
        Title = "Start Vibe Snake Signal School lesson",
        UseStructuredContent = true,
        OutputSchemaType = typeof(StartAgentMatchV5),
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Starts one canonical open-seed Signal School practice. The lesson owns its fixed mode, seed, step cap, instruction, and ordered replay/attempt-evidence requirements. Lessons accept no style contract or rival. Missing, unexpected, or wrong-typed argument names are rejected before this tool runs with the exact mismatch and no state change.")]
    public StartAgentMatchV5 StartLesson(
        [Description("Closed lesson ID from vibesnake://agent/signal-school.")] string lessonId,
        [Description("Set true to mint a one-time same-user named-pipe capability for a read-only local viewer.")] bool watchEnabled = false,
        [Description("Optional public Agent Passport v4 using avatar, accent, and station IDs from vibesnake://agent/identity. Its observation and action profiles must match the host contract.")] AgentPassportV4? passport = null,
        [Description("Control division: four-direction-step-v1 or four-direction-burst-v1.")] string actionProfile = AgentPassportV4.FourDirectionActionProfile) =>
        Execute(() => _registry.StartLesson(
            lessonId,
            watchEnabled,
            passport,
            actionProfile));

    [McpServerTool(
        Name = "observe_match",
        Title = "Observe Vibe Snake match",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentObservationV5),
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Returns the current closed public logical-state observation. It never advances rules state and is not a serialization of the human screen.")]
    public AgentObservationV5 ObserveMatch(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.Observe(matchHandle));

    [McpServerTool(
        Name = "play_move",
        Title = "Play one Vibe Snake move",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentActionResponseV5),
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Submits one four-direction-step-v1 action. Use the exact discovered camelCase arguments, including action. An accepted request advances exactly one rules step. Stale or illegal requests advance none. Missing, unexpected, or wrong-typed argument names are rejected before this tool runs with the exact field mismatch and no state change. Reusing the same idempotency key with the same input returns the original response. When lesson_progress.recommended_next_tool is finish_match, finalize the completed lesson instead of padding steps.")]
    public AgentActionResponseV5 PlayMove(
        [Description("Opaque handle returned by start_match.")] string matchHandle,
        [Description("Unique ASCII token for this intended action, at most 128 characters.")] string idempotencyKey,
        [Description("Exact tick from the observation being acted upon.")] int expectedTick,
        [Description("Exact state hash from the observation being acted upon.")] string expectedStateHash,
        [Description("Action: continue, up, right, down, or left.")] AgentAction action,
        [Description("Optional self-declared public intent: undeclared, seek_food, seek_power, preserve_space, take_risk, or recover. It is presentation-only and never affects rules or verification.")] AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared) =>
        Execute(() => _registry.PlayMove(
                matchHandle,
                idempotencyKey,
                expectedTick,
                expectedStateHash,
                action,
                declaredIntent));

    [McpServerTool(
        Name = "play_burst",
        Title = "Play bounded Vibe Snake burst",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentBurstResponseV5),
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Advances a four-direction-burst-v1 match by at most 16 clock-free steps. Use the exact discovered camelCase arguments, including initialAction and maximumSteps. Missing, unexpected, or wrong-typed argument names are rejected before this tool runs with the exact field mismatch and no state change. The initial action applies once, later steps continue, and execution stops at the first fixed public decision event, selected lesson all-requirements transition, terminal state, match cap, replay failure, or requested bound. When lesson_progress.recommended_next_tool is finish_match, finalize the completed lesson instead of padding steps.")]
    public AgentBurstResponseV5 PlayBurst(
        [Description("Opaque handle returned by start_match.")] string matchHandle,
        [Description("Unique ASCII token for this intended burst, at most 128 characters.")] string idempotencyKey,
        [Description("Exact tick from the observation being acted upon.")] int expectedTick,
        [Description("Exact state hash from the observation being acted upon.")] string expectedStateHash,
        [Description("Initial action: continue, up, right, down, or left. Later steps continue the resulting direction.")] AgentAction initialAction,
        [Description("Maximum accepted rules steps from 1 through 16.")] int maximumSteps,
        [Description("Optional self-declared public intent for the complete burst. It is presentation-only.")] AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared) =>
        Execute(() => _registry.PlayBurst(
            matchHandle,
            idempotencyKey,
            expectedTick,
            expectedStateHash,
            initialAction,
            maximumSteps,
            declaredIntent));

    [McpServerTool(
        Name = "finish_match",
        Title = "Finish Vibe Snake match",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentMatchSummaryV5),
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Explicitly finalizes a running match and returns its verified result. A lesson with all requirements satisfied receives lifecycle completed; any other nonterminal early finish receives lifecycle aborted. Terminal and step-limit runs finalize automatically. Style criteria are factual measurements against optional targets, not pass/fail grades for the match. Calling finish_match again returns the same result.")]
    public AgentMatchSummaryV5 FinishMatch(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.Finish(matchHandle));

    [McpServerTool(
        Name = "get_match_result",
        Title = "Get Vibe Snake result",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentMatchResultStatusV5),
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Returns whether a verified match result is available and includes its public summary when ready. It never advances or finishes a match.")]
    public AgentMatchResultStatusV5 GetMatchResult(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.GetResult(matchHandle));

    [McpServerTool(
        Name = "get_exhibition_receipt",
        Title = "Get Vibe Snake exhibition receipt",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentExhibitionReceiptStatusV1),
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Returns the canonical exhibition receipt for a successfully finalized, verified match. receipt_hash names this exhibition instance and binds the match handle, so a rematch always mints a new one. route_identity_hash names the walked line and reproduces across separate matches and host processes for the same division, seed, and verified replays, so use it to compare same-seed rematches. Presentation display time sits beside both hashes and is never part of either. A live, unverified, or failed-closed match has no receipt. It never advances or finishes a match.")]
    public AgentExhibitionReceiptStatusV1 GetExhibitionReceipt(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.GetExhibitionReceipt(matchHandle));

    [McpServerTool(
        Name = "save_verified_replay",
        Title = "Save verified Vibe Snake replay",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentReplaySaveV1),
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Explicitly saves a completed match's already-verified agent replay and optional rival replay into Vibe Snake's bounded application-owned replay store. It accepts no path and never overwrites different data.")]
    public AgentReplaySaveV1 SaveVerifiedReplay(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.SaveVerifiedReplay(matchHandle));

    [McpServerTool(
        Name = "archive_exhibition",
        Title = "Archive Vibe Snake exhibition",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentExhibitionArchiveStatusV2),
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Explicitly keeps one verified exhibition in Vibe Snake's bounded local archive and returns the archive index. Call save_verified_replay first: an archived exhibition names the saved replay file for every lane it contains, and a rivalry archives both lanes or neither. The write is atomic and bounded to 32 exhibitions and 4,194,304 bytes, whichever binds first; at capacity the oldest are evicted and every dropped exhibition is named. Archiving the same exhibition again writes nothing and reports already_archived, so the call is safe to repeat. It accepts no path, never overwrites a different exhibition under an existing receipt hash, and never advances or finishes a match.")]
    public AgentExhibitionArchiveStatusV2 ArchiveExhibition(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.ArchiveExhibition(matchHandle));

    [McpServerTool(
        Name = "list_exhibitions",
        Title = "List archived Vibe Snake exhibitions",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentExhibitionArchiveListingV1),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Reads the local exhibition archive without writing to it and publishes both of its bounds plus the exact bytes it occupies. Supply routeIdentityHash to narrow the listing to one walked line; the same division, seed, and verified replays reproduce that hash across matches and host processes, so this is how a rematch of a line already kept is recognised. Every listed entry also reports whether its named lane replay files are still on disk. It never advances, finishes, or archives a match.")]
    public AgentExhibitionArchiveListingV1 ListExhibitions(
        [Description("Optional route identity hash to filter by. Use null to list every archived exhibition.")] string? routeIdentityHash = null) =>
        Execute(() => _registry.ListExhibitions(routeIdentityHash));

    [McpServerTool(
        Name = "get_exhibition_story",
        Title = "Get archived Vibe Snake exhibition story",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentExhibitionStoryReportV1),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Builds the recorded-first story for one archived exhibition from its receipt and named lane replay files. Display time is ignored. A missing or disagreeing tape is refused before any highlight is returned. It never writes, never advances a match, and never touches the passport store.")]
    public AgentExhibitionStoryReportV1 GetExhibitionStory(
        [Description("Receipt hash of an archived exhibition.")] string receiptHash) =>
        Execute(() => _registry.GetExhibitionStory(receiptHash));

    [McpServerTool(
        Name = "forget_exhibition",
        Title = "Forget archived Vibe Snake exhibition",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentExhibitionForgetStatusV1),
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Removes one archived exhibition by receipt hash, or clears the archive when receiptHash is null, and returns the archive index afterwards. Every removed exhibition is named in the response. This deletes archive entries only: the saved replay files and every other store are untouched, and no human score, progression, or profile data is reachable from here. Removing something that is not archived writes nothing and reports not_archived, so the call is safe to repeat.")]
    public AgentExhibitionForgetStatusV1 ForgetExhibition(
        [Description("Receipt hash of the exhibition to remove. Use null to clear every archived exhibition.")] string? receiptHash = null) =>
        Execute(() => _registry.ForgetExhibition(receiptHash));

    [McpServerTool(
        Name = "record_passport",
        Title = "Record Vibe Snake agent passport",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentPassportWriteStatusV1),
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Records one verified exhibition against its agent's public identity and returns the passport store index. Supply exactly one of matchHandle or receiptHash. A live handle needs a finalized verified match; a receipt hash must already be in the local exhibition archive. Saved replay files are not required. Recording is idempotent by receipt hash, so repeating the call never inflates a count. A live, unverified, or failed-closed match reports no_verified_receipt. The store is local, bounded to 16 agents, 32 receipts per agent, and 1,048,576 bytes, lives outside the supported Persistence assembly, and never stores a display name, prompt, or human profile. A seventeenth agent is refused rather than evicted.")]
    public AgentPassportWriteStatusV1 RecordPassport(
        [Description("Opaque handle returned by start_match. Use null when recording from an archived receipt hash.")] string? matchHandle = null,
        [Description("Receipt hash of an archived exhibition. Use null when recording from a live match handle.")] string? receiptHash = null) =>
        Execute(() => _registry.RecordPassport(matchHandle, receiptHash));

    [McpServerTool(
        Name = "list_passports",
        Title = "List Vibe Snake agent passports",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentPassportListingV1),
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Reads the local public-identity store without writing to it and publishes both of its bounds plus the exact bytes it occupies. Supply agentId to narrow the listing to one agent. Every listed record is assembled only from verified receipts: exhibition counts, style and lesson tallies, rival ahead/level/behind facts, and milestones that point back at the exhibition that earned them. Ahead, level, and behind are not standings. It never advances or finishes a match.")]
    public AgentPassportListingV1 ListPassports(
        [Description("Optional agent id to filter by. Use null to list every public record.")] string? agentId = null) =>
        Execute(() => _registry.ListPassports(agentId));

    [McpServerTool(
        Name = "forget_passport",
        Title = "Forget Vibe Snake agent passport",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentPassportForgetStatusV1),
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Removes one public agent record by agent id, or clears the store when agentId is null, and returns the index afterwards. Every removed record is named. This deletes passport entries only: the exhibition archive, saved replay files, and every human store are untouched. Removing something that is not recorded writes nothing and reports not_recorded, so the call is safe to repeat.")]
    public AgentPassportForgetStatusV1 ForgetPassport(
        [Description("Agent id of the public record to remove. Use null to clear every public record.")] string? agentId = null) =>
        Execute(() => _registry.ForgetPassport(agentId));

    private static T Execute<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or KeyNotFoundException)
        {
            throw new McpException(exception.Message, exception);
        }
    }
}
