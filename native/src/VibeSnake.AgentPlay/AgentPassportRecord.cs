using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

/// <summary>
/// A factual threshold an agent's public record crossed for the first time.
/// Every milestone names the exhibition that earned it, so none of them is a
/// grade, a ranking, or a claim about skill: each one is a pointer back to a
/// verified receipt that a person can go and check.
/// </summary>
public sealed record AgentPassportMilestoneV1(
    string Schema,
    string MilestoneId,
    string ReceiptHash,
    string RouteIdentityHash)
{
    public const string Contract = "vibesnake-agent-passport-milestone-v1";

    /// <summary>The first exhibition this agent ever had recorded.</summary>
    public const string FirstExhibitionId = "first-exhibition";

    /// <summary>The first exhibition that faced a built-in rival on equal rules.</summary>
    public const string FirstRivalryId = "first-rivalry";

    /// <summary>The first Signal School practice finished with every requirement satisfied.</summary>
    public const string FirstCompletedLessonId = "first-completed-lesson";

    /// <summary>The first Style Contract exhibition that crossed both of its thresholds.</summary>
    public const string FirstAllStyleThresholdsId = "first-all-style-thresholds";

    /// <summary>Every milestone this build can record, in the order it publishes them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        FirstExhibitionId,
        FirstRivalryId,
        FirstCompletedLessonId,
        FirstAllStyleThresholdsId,
    ];
}

/// <summary>
/// What an agent has done with one Style Contract, counted across verified
/// exhibitions. Attempts and threshold crossings are counts of facts, never a
/// rating: a style that was attempted often and crossed rarely says only that.
/// </summary>
public sealed record AgentPassportStyleRecordV1(
    string Schema,
    string StyleContractId,
    int Exhibitions,
    int AllThresholdsReached,
    int BestThresholdsReached)
{
    public const string Contract = "vibesnake-agent-passport-style-record-v1";
}

/// <summary>
/// What an agent has done with one Signal School practice, counted across
/// verified exhibitions.
/// </summary>
public sealed record AgentPassportLessonRecordV1(
    string Schema,
    string LessonId,
    int Exhibitions,
    int AllRequirementsSatisfied,
    int BestRequirementsSatisfied)
{
    public const string Contract = "vibesnake-agent-passport-lesson-record-v1";
}

/// <summary>
/// What an agent has done against one built-in rival on equal rules and an
/// equal seed. Ahead, level, and behind are the three possible outcomes of
/// comparing two scores; they are not a ranking and they do not accumulate into
/// one. AA-08 owns standings, and it owns them separately for a reason.
/// </summary>
public sealed record AgentPassportRivalRecordV1(
    string Schema,
    string RivalPersonalityId,
    int Faced,
    int Ahead,
    int Level,
    int Behind)
{
    public const string Contract = "vibesnake-agent-passport-rival-record-v1";
}

/// <summary>
/// One agent's persistent public record, assembled only from verified
/// exhibition receipts.
///
/// The passport a caller declares when starting a match is ephemeral and
/// unverified: anyone can claim any agent id, display name, or policy version.
/// This record is the opposite by construction. Nothing enters it that did not
/// come from a receipt that recomputed its own canonical hashes, so what it
/// says was earned rather than asserted. It stores no display name for the same
/// reason: a name is a claim, and a claim is not a record.
/// </summary>
public sealed record AgentPassportRecordV1(
    string Schema,
    string AgentId,
    IReadOnlyList<string> PolicyVersions,
    IReadOnlyList<string> DivisionIds,
    int Exhibitions,
    int BestScore,
    IReadOnlyList<AgentPassportStyleRecordV1> Styles,
    IReadOnlyList<AgentPassportLessonRecordV1> Lessons,
    IReadOnlyList<AgentPassportRivalRecordV1> Rivals,
    IReadOnlyList<AgentPassportMilestoneV1> Milestones,
    IReadOnlyList<string> ReceiptHashes,
    string FirstReceiptHash,
    string LatestReceiptHash)
{
    public const string Contract = "vibesnake-agent-passport-record-v1";

    /// <summary>
    /// How many distinct policy versions and divisions one record keeps. An
    /// agent that changes policy every match is a different question from an
    /// agent with a history, and neither should be able to grow this file
    /// without bound.
    /// </summary>
    public const int MaximumTrackedPolicyVersions = 16;

    public const int MaximumTrackedDivisions = 16;

    public const int MaximumRecordedReceipts = AgentPassportDocumentV1.MaximumRecordedReceiptsPerAgent;

    /// <summary>
    /// Starts a record from one verified receipt.
    /// </summary>
    public static AgentPassportRecordV1 FromReceipt(AgentExhibitionReceiptV2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        RequireVerified(receipt);

        var record = new AgentPassportRecordV1(
            Contract,
            receipt.Passport.AgentId,
            [receipt.Passport.PolicyVersion],
            [receipt.Division.DivisionId],
            Exhibitions: 0,
            BestScore: int.MinValue,
            Array.Empty<AgentPassportStyleRecordV1>(),
            Array.Empty<AgentPassportLessonRecordV1>(),
            Array.Empty<AgentPassportRivalRecordV1>(),
            Array.Empty<AgentPassportMilestoneV1>(),
            Array.Empty<string>(),
            receipt.ReceiptHash,
            receipt.ReceiptHash);
        return record.WithReceipt(receipt);
    }

    /// <summary>
    /// Folds one more verified exhibition into this record. The receipt must
    /// belong to the same agent, because a record is one agent's history and
    /// merging two would make it a claim about neither.
    /// </summary>
    public AgentPassportRecordV1 WithReceipt(AgentExhibitionReceiptV2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        RequireVerified(receipt);
        if (!string.Equals(receipt.Passport.AgentId, AgentId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A passport record only accumulates exhibitions from its own agent.",
                nameof(receipt));
        }

        var milestones = Milestones.ToList();
        void Earn(string milestoneId, bool earned)
        {
            if (!earned
                || milestones.Any(existing => string.Equals(
                    existing.MilestoneId,
                    milestoneId,
                    StringComparison.Ordinal)))
            {
                return;
            }

            milestones.Add(new AgentPassportMilestoneV1(
                AgentPassportMilestoneV1.Contract,
                milestoneId,
                receipt.ReceiptHash,
                receipt.RouteIdentityHash));
        }

        Earn(AgentPassportMilestoneV1.FirstExhibitionId, true);
        Earn(
            AgentPassportMilestoneV1.FirstRivalryId,
            receipt.RivalPersonalityId is not null && receipt.RivalScore is not null);
        Earn(
            AgentPassportMilestoneV1.FirstCompletedLessonId,
            receipt.LessonOutcome is { AllRequirementsSatisfied: true });
        Earn(
            AgentPassportMilestoneV1.FirstAllStyleThresholdsId,
            receipt.StyleOutcome is { AllThresholdsReached: true });

        return this with
        {
            PolicyVersions = Extend(PolicyVersions, receipt.Passport.PolicyVersion, MaximumTrackedPolicyVersions),
            DivisionIds = Extend(DivisionIds, receipt.Division.DivisionId, MaximumTrackedDivisions),
            Exhibitions = Exhibitions + 1,
            BestScore = Math.Max(BestScore, receipt.Score),
            Styles = FoldStyle(Styles, receipt.StyleOutcome),
            Lessons = FoldLesson(Lessons, receipt.LessonOutcome),
            Rivals = FoldRival(Rivals, receipt),
            Milestones = milestones.AsReadOnly(),
            ReceiptHashes = ReceiptHashes.Append(receipt.ReceiptHash).ToArray(),
            LatestReceiptHash = receipt.ReceiptHash,
        };
    }

    /// <summary>
    /// Whether this record still only describes facts it could have earned.
    /// Counts cannot exceed the exhibitions that produced them, and a milestone
    /// cannot exist twice or under an unknown identifier.
    /// </summary>
    public bool IsSelfConsistent() =>
        string.Equals(Schema, Contract, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(AgentId)
        && Exhibitions > 0
        && PolicyVersions.Count is > 0 and <= MaximumTrackedPolicyVersions
        && DivisionIds.Count is > 0 and <= MaximumTrackedDivisions
        && PolicyVersions.All(value => !string.IsNullOrWhiteSpace(value))
        && DivisionIds.All(value => !string.IsNullOrWhiteSpace(value))
        && PolicyVersions
            .Distinct(StringComparer.Ordinal)
            .Count() == PolicyVersions.Count
        && DivisionIds
            .Distinct(StringComparer.Ordinal)
            .Count() == DivisionIds.Count
        && Styles.All(style =>
            string.Equals(style.Schema, AgentPassportStyleRecordV1.Contract, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(style.StyleContractId)
            && style.Exhibitions > 0
            && style.Exhibitions <= Exhibitions
            && style.AllThresholdsReached <= style.Exhibitions
            && style.BestThresholdsReached is >= 0 and <= 2)
        && Styles
            .Select(style => style.StyleContractId)
            .Distinct(StringComparer.Ordinal)
            .Count() == Styles.Count
        && Lessons.All(lesson =>
            string.Equals(lesson.Schema, AgentPassportLessonRecordV1.Contract, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(lesson.LessonId)
            && lesson.Exhibitions > 0
            && lesson.Exhibitions <= Exhibitions
            && lesson.AllRequirementsSatisfied <= lesson.Exhibitions
            && lesson.BestRequirementsSatisfied is >= 0 and <= 2)
        && Lessons
            .Select(lesson => lesson.LessonId)
            .Distinct(StringComparer.Ordinal)
            .Count() == Lessons.Count
        && Rivals.All(rival =>
            string.Equals(rival.Schema, AgentPassportRivalRecordV1.Contract, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(rival.RivalPersonalityId)
            && rival.Faced > 0
            && rival.Faced <= Exhibitions
            && rival.Ahead + rival.Level + rival.Behind == rival.Faced)
        && Rivals
            .Select(rival => rival.RivalPersonalityId)
            .Distinct(StringComparer.Ordinal)
            .Count() == Rivals.Count
        && Milestones.Count <= AgentPassportMilestoneV1.All.Count
        && Milestones.All(milestone =>
            string.Equals(milestone.Schema, AgentPassportMilestoneV1.Contract, StringComparison.Ordinal)
            && AgentPassportMilestoneV1.All.Contains(milestone.MilestoneId)
            && !string.IsNullOrWhiteSpace(milestone.ReceiptHash)
            && !string.IsNullOrWhiteSpace(milestone.RouteIdentityHash))
        && Milestones
            .Select(milestone => milestone.MilestoneId)
            .Distinct(StringComparer.Ordinal)
            .Count() == Milestones.Count
        && Milestones.Any(milestone =>
            milestone.MilestoneId == AgentPassportMilestoneV1.FirstExhibitionId
            && string.Equals(milestone.ReceiptHash, FirstReceiptHash, StringComparison.Ordinal))
        && ReceiptHashes.Count == Exhibitions
        && ReceiptHashes.Count <= MaximumRecordedReceipts
        && ReceiptHashes
            .Distinct(StringComparer.Ordinal)
            .Count() == ReceiptHashes.Count
        && ReceiptHashes.Count > 0
        && string.Equals(ReceiptHashes[0], FirstReceiptHash, StringComparison.Ordinal)
        && string.Equals(
            ReceiptHashes[^1],
            LatestReceiptHash,
            StringComparison.Ordinal)
        && ReceiptHashes.Contains(FirstReceiptHash, StringComparer.Ordinal)
        && ReceiptHashes.Contains(LatestReceiptHash, StringComparer.Ordinal)
        && (Exhibitions != 1
            || string.Equals(FirstReceiptHash, LatestReceiptHash, StringComparison.Ordinal))
        && !string.IsNullOrWhiteSpace(FirstReceiptHash)
        && !string.IsNullOrWhiteSpace(LatestReceiptHash);

    private static void RequireVerified(AgentExhibitionReceiptV2 receipt)
    {
        // A receipt that cannot recompute its own canonical hashes is not
        // evidence of anything, and a public record built from it would be a
        // claim wearing a hash.
        if (!AgentExhibitionReceipt.HasCanonicalHash(receipt))
        {
            throw new ArgumentException(
                "A passport record only accepts a receipt that recomputes its own canonical hashes.",
                nameof(receipt));
        }
    }

    private static IReadOnlyList<string> Extend(
        IReadOnlyList<string> existing,
        string value,
        int maximum)
    {
        if (existing.Contains(value, StringComparer.Ordinal))
        {
            return existing;
        }

        // Oldest out first, so a record that keeps changing identity keeps the
        // identities it is using now rather than the ones it started with.
        var extended = existing.Append(value).ToList();
        while (extended.Count > maximum)
        {
            extended.RemoveAt(0);
        }

        return extended.AsReadOnly();
    }

    private static IReadOnlyList<AgentPassportStyleRecordV1> FoldStyle(
        IReadOnlyList<AgentPassportStyleRecordV1> existing,
        AgentStyleOutcomeV3? outcome)
    {
        if (outcome is null)
        {
            return existing;
        }

        var folded = existing.ToList();
        var index = folded.FindIndex(record => string.Equals(
            record.StyleContractId,
            outcome.ContractId,
            StringComparison.Ordinal));
        if (index < 0)
        {
            folded.Add(new AgentPassportStyleRecordV1(
                AgentPassportStyleRecordV1.Contract,
                outcome.ContractId,
                Exhibitions: 1,
                outcome.AllThresholdsReached ? 1 : 0,
                outcome.ThresholdsReached));
            return folded.AsReadOnly();
        }

        var current = folded[index];
        folded[index] = current with
        {
            Exhibitions = current.Exhibitions + 1,
            AllThresholdsReached = current.AllThresholdsReached
                + (outcome.AllThresholdsReached ? 1 : 0),
            BestThresholdsReached = Math.Max(
                current.BestThresholdsReached,
                outcome.ThresholdsReached),
        };
        return folded.AsReadOnly();
    }

    private static IReadOnlyList<AgentPassportLessonRecordV1> FoldLesson(
        IReadOnlyList<AgentPassportLessonRecordV1> existing,
        AgentLessonOutcomeV3? outcome)
    {
        if (outcome is null)
        {
            return existing;
        }

        var folded = existing.ToList();
        var index = folded.FindIndex(record => string.Equals(
            record.LessonId,
            outcome.LessonId,
            StringComparison.Ordinal));
        if (index < 0)
        {
            folded.Add(new AgentPassportLessonRecordV1(
                AgentPassportLessonRecordV1.Contract,
                outcome.LessonId,
                Exhibitions: 1,
                outcome.AllRequirementsSatisfied ? 1 : 0,
                outcome.RequirementsSatisfied));
            return folded.AsReadOnly();
        }

        var current = folded[index];
        folded[index] = current with
        {
            Exhibitions = current.Exhibitions + 1,
            AllRequirementsSatisfied = current.AllRequirementsSatisfied
                + (outcome.AllRequirementsSatisfied ? 1 : 0),
            BestRequirementsSatisfied = Math.Max(
                current.BestRequirementsSatisfied,
                outcome.RequirementsSatisfied),
        };
        return folded.AsReadOnly();
    }

    private static IReadOnlyList<AgentPassportRivalRecordV1> FoldRival(
        IReadOnlyList<AgentPassportRivalRecordV1> existing,
        AgentExhibitionReceiptV2 receipt)
    {
        if (receipt.RivalPersonalityId is not { } rivalId
            || receipt.RivalScore is not { } rivalScore)
        {
            return existing;
        }

        var ahead = receipt.Score > rivalScore ? 1 : 0;
        var level = receipt.Score == rivalScore ? 1 : 0;
        var behind = receipt.Score < rivalScore ? 1 : 0;
        var folded = existing.ToList();
        var index = folded.FindIndex(record => string.Equals(
            record.RivalPersonalityId,
            rivalId,
            StringComparison.Ordinal));
        if (index < 0)
        {
            folded.Add(new AgentPassportRivalRecordV1(
                AgentPassportRivalRecordV1.Contract,
                rivalId,
                Faced: 1,
                ahead,
                level,
                behind));
            return folded.AsReadOnly();
        }

        var current = folded[index];
        folded[index] = current with
        {
            Faced = current.Faced + 1,
            Ahead = current.Ahead + ahead,
            Level = current.Level + level,
            Behind = current.Behind + behind,
        };
        return folded.AsReadOnly();
    }
}
