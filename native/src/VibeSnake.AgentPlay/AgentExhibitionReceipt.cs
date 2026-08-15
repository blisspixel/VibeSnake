using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

/// <summary>
/// One accepted mutation's public presentation facts. The declared intent is a
/// self-reported spectator label that never changed rules, score, or verification.
/// </summary>
public sealed record AgentAcceptedPresentationEventV1(
    int Ordinal,
    int Tick,
    AgentAction Action,
    AgentPublicIntent DeclaredIntent);

/// <summary>
/// The closed competition division a receipt belongs to. Results from different
/// modes, seed visibilities, observation profiles, or action profiles are never
/// comparable. AA-08 owns ranking-time division manifests; this identity only
/// records which division an exhibition actually ran in.
/// </summary>
public sealed record AgentDivisionIdentityV1(
    string Schema,
    string DivisionId,
    string RulesetId,
    int RulesVersion,
    string ModeId,
    int ModeVersion,
    string ConfigHashAlgorithm,
    string ConfigHash,
    AgentSeedVisibility SeedVisibility,
    string ObservationProfile,
    string ActionProfile)
{
    public const string Contract = "vibesnake-agent-division-identity-v1";

    internal static AgentDivisionIdentityV1 FromResult(AgentMatchResultV5 result) =>
        new(
            Contract,
            ComposeDivisionId(
                result.ModeId,
                result.ModeVersion,
                result.SeedVisibility,
                result.Passport.ObservationProfile,
                result.Passport.ActionProfile),
            result.RulesetId,
            result.RulesVersion,
            result.ModeId,
            result.ModeVersion,
            result.ConfigHashAlgorithm,
            result.ConfigHash,
            result.SeedVisibility,
            result.Passport.ObservationProfile,
            result.Passport.ActionProfile);

    internal static string ComposeDivisionId(
        string modeId,
        int modeVersion,
        AgentSeedVisibility seedVisibility,
        string observationProfile,
        string actionProfile) =>
        string.Join(
            '|',
            $"{modeId}@{modeVersion.ToString(CultureInfo.InvariantCulture)}",
            seedVisibility == AgentSeedVisibility.Open ? "open" : "blind",
            observationProfile,
            actionProfile);
}

/// <summary>
/// A transport-neutral canonical exhibition receipt. It hash-links both verified
/// lane replays, the division identity, the public passport, the replay-derived
/// style and lesson outcomes, and the accepted presentation events into one
/// <see cref="ReceiptHash"/>. Presentation display time is carried beside that hash
/// and is deliberately excluded from it, so the same exhibition always produces the
/// same identity no matter when it is shown.
///
/// Two identities are published because they answer different questions.
/// <see cref="ReceiptHash"/> names this exhibition instance and includes the match
/// handle, so a rematch of the same line always mints a new one.
/// <see cref="RouteIdentityHash"/> names the line itself: the same division, seed,
/// verified replays, and evidence outcome reproduce it across separate matches and
/// separate host processes. Use it to recognise "I already walked this line".
/// </summary>
public sealed record AgentExhibitionReceiptV2(
    string Schema,
    string MatchId,
    AgentDivisionIdentityV1 Division,
    AgentPassportV4 Passport,
    AgentMatchLifecycle Lifecycle,
    AgentMatchEndReason EndReason,
    string GameplaySeed,
    int FinalTick,
    RunStatus RunStatus,
    DeathCause DeathCause,
    int Score,
    string FinalStateHash,
    string AgentReplayPayloadHash,
    ReplayVerificationCode AgentReplayVerificationCode,
    string? RivalPersonalityId,
    string? RivalReplayPayloadHash,
    ReplayVerificationCode? RivalReplayVerificationCode,
    int? RivalScore,
    AgentStyleOutcomeV3? StyleOutcome,
    AgentLessonOutcomeV3? LessonOutcome,
    IReadOnlyList<AgentAcceptedPresentationEventV1> AcceptedPresentationEvents,
    string ReceiptHash,
    string RouteIdentityHash,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DisplayTimeUtc = null)
{
    public const string Contract = "vibesnake-agent-exhibition-receipt-v2";

    /// <summary>
    /// Attaches a presentation-only display time. Both canonical hashes are
    /// unchanged because display time is never part of exhibition identity.
    /// </summary>
    public AgentExhibitionReceiptV2 WithDisplayTime(string? displayTimeUtc) =>
        this with { DisplayTimeUtc = displayTimeUtc };
}

public static class AgentExhibitionReceipt
{
    private const string ReceiptDomain = "vibesnake-agent-exhibition-receipt-v2";
    private const string RouteDomain = "vibesnake-agent-exhibition-route-v1";
    private const string LineSeparator = "\n";

    /// <summary>
    /// Builds the canonical receipt for one successfully finalized, verified result.
    /// A failed-closed or unverified result has no exhibition identity and returns null.
    /// </summary>
    public static AgentExhibitionReceiptV2? TryCreate(
        AgentMatchResultV5 result,
        IReadOnlyList<AgentAcceptedPresentationEventV1> acceptedPresentationEvents)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(acceptedPresentationEvents);
        if (result.ReplayVerificationCode != ReplayVerificationCode.Verified
            || result.Lifecycle is AgentMatchLifecycle.AwaitingAction
                or AgentMatchLifecycle.FailedClosed)
        {
            return null;
        }

        // A rivalry is only receipted when both lanes verified independently.
        if (result.Rival is { } unverifiedRival
            && unverifiedRival.ReplayVerificationCode != ReplayVerificationCode.Verified)
        {
            return null;
        }

        var events = Array.AsReadOnly(acceptedPresentationEvents.ToArray());
        var receipt = new AgentExhibitionReceiptV2(
            AgentExhibitionReceiptV2.Contract,
            result.MatchId,
            AgentDivisionIdentityV1.FromResult(result),
            result.Passport,
            result.Lifecycle,
            result.EndReason,
            result.GameplaySeed.ToString(CultureInfo.InvariantCulture),
            result.FinalTick,
            result.RunStatus,
            result.DeathCause,
            result.Score,
            result.FinalStateHash,
            result.ReplayPayloadHash,
            result.ReplayVerificationCode,
            result.Rival?.PersonalityId,
            result.Rival?.ReplayPayloadHash,
            result.Rival?.ReplayVerificationCode,
            result.Rival?.Score,
            result.StyleOutcome,
            result.LessonOutcome,
            events,
            ComputeReceiptHash(
                result,
                AgentDivisionIdentityV1.FromResult(result),
                events),
            ComputeRouteIdentityHash(
                result,
                AgentDivisionIdentityV1.FromResult(result)));
        return receipt;
    }

    /// <summary>
    /// Recomputes both canonical hashes and confirms that the receipt still
    /// describes itself. Display time is intentionally excluded from both.
    /// </summary>
    public static bool HasCanonicalHash(AgentExhibitionReceiptV2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return string.Equals(
            receipt.ReceiptHash,
            ComputeReceiptHash(receipt),
            StringComparison.Ordinal)
            && string.Equals(
                receipt.RouteIdentityHash,
                ComputeRouteIdentityHash(receipt),
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Hashes the line rather than the visit. This deliberately omits the match
    /// handle, the caller-declared passport, presentation events, and any
    /// attempt-evidence value derived from idempotency keys, so replaying the same
    /// seed and route in a new match or a new host process reproduces it exactly.
    /// </summary>
    private static string ComputeRouteIdentityHash(
        AgentMatchResultV5 result,
        AgentDivisionIdentityV1 division) =>
        ComputeRouteIdentityHash(
            division,
            result.GameplaySeed.ToString(CultureInfo.InvariantCulture),
            result.EndReason,
            result.RunStatus,
            result.DeathCause,
            result.Score,
            result.FinalTick,
            result.FinalStateHash,
            result.ReplayPayloadHash,
            result.Rival?.PersonalityId,
            result.Rival?.ReplayPayloadHash,
            result.Rival?.Score,
            result.StyleOutcome?.ContractId,
            result.StyleOutcome?.AllThresholdsReached,
            result.StyleOutcome?.ThresholdsReached,
            result.LessonOutcome?.LessonId,
            result.LessonOutcome?.AllRequirementsSatisfied,
            result.LessonOutcome?.RequirementsSatisfied);

    private static string ComputeRouteIdentityHash(AgentExhibitionReceiptV2 receipt) =>
        ComputeRouteIdentityHash(
            receipt.Division,
            receipt.GameplaySeed,
            receipt.EndReason,
            receipt.RunStatus,
            receipt.DeathCause,
            receipt.Score,
            receipt.FinalTick,
            receipt.FinalStateHash,
            receipt.AgentReplayPayloadHash,
            receipt.RivalPersonalityId,
            receipt.RivalReplayPayloadHash,
            receipt.RivalScore,
            receipt.StyleOutcome?.ContractId,
            receipt.StyleOutcome?.AllThresholdsReached,
            receipt.StyleOutcome?.ThresholdsReached,
            receipt.LessonOutcome?.LessonId,
            receipt.LessonOutcome?.AllRequirementsSatisfied,
            receipt.LessonOutcome?.RequirementsSatisfied);

    private static string ComputeRouteIdentityHash(
        AgentDivisionIdentityV1 division,
        string gameplaySeed,
        AgentMatchEndReason endReason,
        RunStatus runStatus,
        DeathCause deathCause,
        int score,
        int finalTick,
        string finalStateHash,
        string agentReplayPayloadHash,
        string? rivalPersonalityId,
        string? rivalReplayPayloadHash,
        int? rivalScore,
        string? styleContractId,
        bool? styleAllThresholdsReached,
        int? styleThresholdsReached,
        string? lessonId,
        bool? lessonAllRequirementsSatisfied,
        int? lessonRequirementsSatisfied)
    {
        // A fixed newline separator keeps the payload identical on every platform.
        var payload = string.Join(
            LineSeparator,
            RouteDomain,
            division.DivisionId,
            $"{division.RulesetId}@{Number(division.RulesVersion)}",
            $"{division.ConfigHashAlgorithm}:{division.ConfigHash}",
            gameplaySeed,
            string.Join(
                '|',
                Number((byte)endReason),
                Number((byte)runStatus),
                Number((byte)deathCause),
                Number(score),
                Number(finalTick)),
            finalStateHash,
            agentReplayPayloadHash,
            string.Join(
                '|',
                Optional(rivalPersonalityId),
                Optional(rivalReplayPayloadHash),
                rivalScore is { } rivalPoints ? Number(rivalPoints) : "-"),
            string.Join(
                '|',
                Optional(styleContractId),
                Flag(styleAllThresholdsReached),
                styleThresholdsReached is { } styleCount ? Number(styleCount) : "-"),
            string.Join(
                '|',
                Optional(lessonId),
                Flag(lessonAllRequirementsSatisfied),
                lessonRequirementsSatisfied is { } lessonCount ? Number(lessonCount) : "-"))
            + LineSeparator;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string ComputeReceiptHash(
        AgentMatchResultV5 result,
        AgentDivisionIdentityV1 division,
        IReadOnlyList<AgentAcceptedPresentationEventV1> events) =>
        ComputeReceiptHash(
            result.MatchId,
            division,
            result.Passport,
            result.Lifecycle,
            result.EndReason,
            result.GameplaySeed.ToString(CultureInfo.InvariantCulture),
            result.FinalTick,
            result.RunStatus,
            result.DeathCause,
            result.Score,
            result.FinalStateHash,
            result.ReplayPayloadHash,
            result.ReplayVerificationCode,
            result.Rival?.PersonalityId,
            result.Rival?.ReplayPayloadHash,
            result.Rival?.ReplayVerificationCode,
            result.Rival?.Score,
            result.StyleOutcome?.ReplayPayloadHash,
            result.StyleOutcome?.AllThresholdsReached,
            result.StyleOutcome?.ThresholdsReached,
            result.LessonOutcome?.EvidenceHash,
            result.LessonOutcome?.AllRequirementsSatisfied,
            result.LessonOutcome?.RequirementsSatisfied,
            events);

    private static string ComputeReceiptHash(AgentExhibitionReceiptV2 receipt) =>
        ComputeReceiptHash(
            receipt.MatchId,
            receipt.Division,
            receipt.Passport,
            receipt.Lifecycle,
            receipt.EndReason,
            receipt.GameplaySeed,
            receipt.FinalTick,
            receipt.RunStatus,
            receipt.DeathCause,
            receipt.Score,
            receipt.FinalStateHash,
            receipt.AgentReplayPayloadHash,
            receipt.AgentReplayVerificationCode,
            receipt.RivalPersonalityId,
            receipt.RivalReplayPayloadHash,
            receipt.RivalReplayVerificationCode,
            receipt.RivalScore,
            receipt.StyleOutcome?.ReplayPayloadHash,
            receipt.StyleOutcome?.AllThresholdsReached,
            receipt.StyleOutcome?.ThresholdsReached,
            receipt.LessonOutcome?.EvidenceHash,
            receipt.LessonOutcome?.AllRequirementsSatisfied,
            receipt.LessonOutcome?.RequirementsSatisfied,
            receipt.AcceptedPresentationEvents);

    private static string ComputeReceiptHash(
        string matchId,
        AgentDivisionIdentityV1 division,
        AgentPassportV4 passport,
        AgentMatchLifecycle lifecycle,
        AgentMatchEndReason endReason,
        string gameplaySeed,
        int finalTick,
        RunStatus runStatus,
        DeathCause deathCause,
        int score,
        string finalStateHash,
        string agentReplayPayloadHash,
        ReplayVerificationCode agentReplayVerificationCode,
        string? rivalPersonalityId,
        string? rivalReplayPayloadHash,
        ReplayVerificationCode? rivalReplayVerificationCode,
        int? rivalScore,
        string? styleReplayPayloadHash,
        bool? styleAllThresholdsReached,
        int? styleThresholdsReached,
        string? lessonEvidenceHash,
        bool? lessonAllRequirementsSatisfied,
        int? lessonRequirementsSatisfied,
        IReadOnlyList<AgentAcceptedPresentationEventV1> events)
    {
        var builder = new StringBuilder();
        builder.Append(ReceiptDomain).Append('\n')
            .Append(matchId).Append('\n')
            .Append(division.DivisionId).Append('\n')
            .Append(division.RulesetId).Append('@').Append(Number(division.RulesVersion))
            .Append('\n')
            .Append(division.ConfigHashAlgorithm).Append(':').Append(division.ConfigHash)
            .Append('\n')
            .Append(passport.AgentId).Append('|').Append(passport.PolicyVersion)
            .Append('|').Append(passport.AvatarId)
            .Append('|').Append(passport.AccentId)
            .Append('|').Append(passport.StationId).Append('\n')
            .Append(Number((byte)lifecycle)).Append('|').Append(Number((byte)endReason))
            .Append('\n')
            .Append(gameplaySeed).Append('\n')
            .Append(Number(finalTick)).Append('|').Append(Number((byte)runStatus))
            .Append('|').Append(Number((byte)deathCause))
            .Append('|').Append(Number(score)).Append('\n')
            .Append(finalStateHash).Append('\n')
            .Append(agentReplayPayloadHash).Append('|')
            .Append(Number((byte)agentReplayVerificationCode)).Append('\n')
            .Append(Optional(rivalPersonalityId)).Append('|')
            .Append(Optional(rivalReplayPayloadHash)).Append('|')
            .Append(rivalReplayVerificationCode is { } rivalCode
                ? Number((byte)rivalCode)
                : "-")
            .Append('|').Append(rivalScore is { } rivalPoints ? Number(rivalPoints) : "-")
            .Append('\n')
            .Append(Optional(styleReplayPayloadHash)).Append('|')
            .Append(Flag(styleAllThresholdsReached)).Append('|')
            .Append(styleThresholdsReached is { } styleCount ? Number(styleCount) : "-")
            .Append('\n')
            .Append(Optional(lessonEvidenceHash)).Append('|')
            .Append(Flag(lessonAllRequirementsSatisfied)).Append('|')
            .Append(lessonRequirementsSatisfied is { } lessonCount
                ? Number(lessonCount)
                : "-")
            .Append('\n')
            .Append(Number(events.Count)).Append('\n');
        foreach (var item in events)
        {
            builder.Append(Number(item.Ordinal))
                .Append('|').Append(Number(item.Tick))
                .Append('|').Append(Number((byte)item.Action))
                .Append('|').Append(Number((byte)item.DeclaredIntent))
                .Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Optional(string? value) =>
        string.IsNullOrEmpty(value) ? "-" : value;

    private static string Flag(bool? value) =>
        value is null ? "-" : value.Value ? "1" : "0";
}
