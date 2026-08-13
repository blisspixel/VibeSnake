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
        OutputSchemaType = typeof(StartAgentMatchV1),
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Starts one isolated, clock-free Vibe Snake agent match and returns its explicit opaque handle plus initial public observation. Use only classic or vibe. Blind matches reject caller-selected seeds.")]
    public StartAgentMatchV1 StartMatch(
        [Description("Official mode ID: classic or vibe.")] string modeId,
        [Description("Seed division: open or blind.")] AgentSeedVisibility seedVisibility,
        [Description("Optional unsigned 64-bit decimal seed for open matches. Use null to let the host generate one. Must be null for blind matches.")] string? gameplaySeed = null,
        [Description("Optional rules-step cap from 1 through 2000. Use null for 2000.")] int? maximumSteps = null,
        [Description("Optional style contract: stillwater, crownchaser, edge-prophet, mutagenist, or redline. Mode restrictions are enforced.")] string? styleContractId = null,
        [Description("Optional built-in rival personality ID. Both lanes use the same seed and exact rules configuration.")] string? rivalPersonalityId = null,
        [Description("Set true to mint a one-time same-user named-pipe capability for a read-only local viewer.")] bool watchEnabled = false,
        [Description("Optional public Agent Passport. IDs are bounded tokens, the display name is presentation-only, and its action profile must match actionProfile.")] AgentPassportV1? passport = null,
        [Description("Control division: four-direction-step-v1 or four-direction-burst-v1. The default preserves one-step play.")] string actionProfile = AgentPassportV1.FourDirectionActionProfile) =>
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
        Name = "observe_match",
        Title = "Observe Vibe Snake match",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentObservationV1),
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Returns the current closed public logical-state observation. It never advances rules state and is not a serialization of the human screen.")]
    public AgentObservationV1 ObserveMatch(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.Observe(matchHandle));

    [McpServerTool(
        Name = "play_move",
        Title = "Play one Vibe Snake move",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentActionResponseV1),
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Submits up, right, down, left, or continue with an optional closed public intent. An accepted request advances exactly one rules step. Stale or illegal requests advance none. Reusing the same idempotency key with the same input returns the original response.")]
    public AgentActionResponseV1 PlayMove(
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
        OutputSchemaType = typeof(AgentBurstResponseV1),
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Advances a four-direction-burst-v1 match by at most 16 clock-free steps. The initial action applies once, later steps continue, and execution stops at the first fixed public decision event, terminal state, match cap, replay failure, or requested bound.")]
    public AgentBurstResponseV1 PlayBurst(
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
        OutputSchemaType = typeof(AgentMatchSummaryV1),
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Explicitly ends a running match, finalizes a nonterminal verified replay, and returns the result. Calling it again returns the same result.")]
    public AgentMatchSummaryV1 FinishMatch(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.Finish(matchHandle));

    [McpServerTool(
        Name = "get_match_result",
        Title = "Get Vibe Snake result",
        UseStructuredContent = true,
        OutputSchemaType = typeof(AgentMatchResultStatusV1),
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Returns whether a verified match result is available and includes its public summary when ready. It never advances or finishes a match.")]
    public AgentMatchResultStatusV1 GetMatchResult(
        [Description("Opaque handle returned by start_match.")] string matchHandle) =>
        Execute(() => _registry.GetResult(matchHandle));

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
