using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

/// <summary>
/// Why one verified exhibition is or is not a qualifying result. The class is
/// a fact about the receipt: a voluntary finish is not a standing, and a
/// completed practice is not a qualification-time score.
/// </summary>
public sealed record AgentQualificationEligibilityV1(
    string Schema,
    string ReceiptHash,
    string AgentId,
    string PolicyVersion,
    string DivisionId,
    AgentQualificationClass Class,
    bool Qualifying,
    AgentDeckKind? DeckKind,
    AgentMatchEndReason EndReason)
{
    public const string Contract = "vibesnake-agent-qualification-eligibility-v1";
}

/// <summary>
/// Practice versus qualification-time completion of one Signal School lesson.
/// The gap is qualification-time completes minus practice completes, so a
/// negative gap means the agent completed the practice board and not the
/// published qualification-time boards.
/// </summary>
public sealed record AgentGeneralizationRowV1(
    string Schema,
    string LessonId,
    int PracticeComplete,
    int QualificationTimeComplete,
    int Gap)
{
    public const string Contract = "vibesnake-agent-generalization-row-v1";
}

/// <summary>
/// One agent's qualifying results in one division and one policy version.
/// Divisions and policy versions are never mixed. Ahead, level, and behind
/// from the passport are not this row.
/// </summary>
public sealed record AgentStandingRowV1(
    string Schema,
    string DivisionId,
    string PolicyVersion,
    string AgentId,
    int QualifyingCount,
    int BestScore,
    int BestFinalTick,
    string BestReceiptHash)
{
    public const string Contract = "vibesnake-agent-standing-row-v1";
}

/// <summary>
/// One qualifying rivalry evaluated as Rival Breaker. Characteristic terms
/// come from the closed catalog, not from the rival's last score.
/// </summary>
public sealed record AgentRivalBreakerRowV1(
    string Schema,
    string ReceiptHash,
    string AgentId,
    string RivalPersonalityId,
    AgentRivalBreakerKind Kind,
    int AgentScore,
    int RivalScore,
    string? StyleContractId)
{
    public const string Contract = "vibesnake-agent-rival-breaker-row-v1";
}

/// <summary>
/// The archive-bound local qualification report. It invents no second identity:
/// every row points at a receipt already kept, classified against the immutable
/// division manifest and the public decks. Building it never writes the
/// archive, the passport store, or human player data.
/// </summary>
public sealed record AgentQualificationReportV1(
    string Schema,
    AgentDivisionManifestV1 Manifest,
    AgentQualificationDecksV1 Decks,
    IReadOnlyList<AgentQualificationEligibilityV1> Eligibility,
    IReadOnlyList<AgentGeneralizationRowV1> Generalization,
    IReadOnlyList<AgentStandingRowV1> Standings,
    IReadOnlyList<AgentRivalBreakerRowV1> RivalBreakers)
{
    public const string Contract = "vibesnake-agent-qualification-report-v1";

    public static AgentQualificationReportV1 FromArchive(
        IReadOnlyList<AgentArchivedExhibitionV2> entries,
        string? agentId = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var receipts = entries
            .Select(entry => entry.Receipt)
            .Where(receipt => agentId is null
                || string.Equals(
                    receipt.Passport.AgentId,
                    agentId,
                    StringComparison.Ordinal))
            .ToArray();
        var eligibility = receipts
            .Select(Classify)
            .ToArray();
        return new AgentQualificationReportV1(
            Contract,
            AgentQualificationCatalog.Manifest,
            AgentQualificationCatalog.Decks,
            eligibility,
            Generalize(receipts, eligibility),
            StandingsFrom(eligibility, receipts),
            RivalBreakersFrom(receipts));
    }

    public static AgentQualificationEligibilityV1 Classify(AgentExhibitionReceiptV2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var deck = AgentQualificationCatalog.DeckKindOf(receipt);
        var classKind = ClassifyReceipt(receipt);
        return new AgentQualificationEligibilityV1(
            AgentQualificationEligibilityV1.Contract,
            receipt.ReceiptHash,
            receipt.Passport.AgentId,
            receipt.Passport.PolicyVersion,
            receipt.Division.DivisionId,
            classKind,
            classKind is AgentQualificationClass.QualifyingTerminal
                or AgentQualificationClass.QualifyingCapped,
            deck,
            receipt.EndReason);
    }

    public static AgentRivalBreakerKind RivalBreakerKind(AgentExhibitionReceiptV2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.RivalPersonalityId is null || receipt.RivalScore is null)
        {
            return AgentRivalBreakerKind.NotARivalry;
        }

        var eligibility = Classify(receipt);
        if (!eligibility.Qualifying)
        {
            return AgentRivalBreakerKind.NonQualifying;
        }

        var relation = receipt.Score > receipt.RivalScore.Value
            ? AgentScoreRelation.Ahead
            : receipt.Score == receipt.RivalScore.Value
                ? AgentScoreRelation.Level
                : AgentScoreRelation.Behind;
        if (relation == AgentScoreRelation.Behind)
        {
            return AgentRivalBreakerKind.Behind;
        }

        if (relation == AgentScoreRelation.Level)
        {
            return AgentRivalBreakerKind.Level;
        }

        var terms = AgentQualificationCatalog.TermsFor(receipt.RivalPersonalityId);
        if (terms.StyleContractId is { } style
            && receipt.StyleOutcome is { } outcome
            && string.Equals(outcome.ContractId, style, StringComparison.Ordinal)
            && outcome.Criteria.Count > 0
            && outcome.Criteria[0].ThresholdReached)
        {
            return AgentRivalBreakerKind.Broken;
        }

        if (terms.StyleContractId is null)
        {
            return AgentRivalBreakerKind.Broken;
        }

        return AgentRivalBreakerKind.BeatScore;
    }

    private static AgentQualificationClass ClassifyReceipt(AgentExhibitionReceiptV2 receipt)
    {
        if (receipt.EndReason is AgentMatchEndReason.None or AgentMatchEndReason.ReplayFailure
            || !AgentQualificationCatalog.IsPublishedDivision(receipt.Division.DivisionId))
        {
            return AgentQualificationClass.Ineligible;
        }

        if (receipt.LessonOutcome is { } lesson)
        {
            var definition = AgentSignalSchoolCatalog.Get(lesson.LessonId);
            if (AgentQualificationCatalog.TryParseSeed(receipt.GameplaySeed, out var seed)
                && seed == definition.PracticeSeed)
            {
                return lesson.AllRequirementsSatisfied
                    ? AgentQualificationClass.PracticeComplete
                    : AgentQualificationClass.PracticeIncomplete;
            }
        }

        return receipt.EndReason switch
        {
            AgentMatchEndReason.AgentFinished => AgentQualificationClass.NonQualifyingFinish,
            AgentMatchEndReason.StepLimit => AgentQualificationClass.QualifyingCapped,
            AgentMatchEndReason.RulesTerminal => AgentQualificationClass.QualifyingTerminal,
            _ => AgentQualificationClass.Ineligible,
        };
    }

    private static AgentGeneralizationRowV1[] Generalize(
        IReadOnlyList<AgentExhibitionReceiptV2> receipts,
        IReadOnlyList<AgentQualificationEligibilityV1> eligibility)
    {
        var byHash = eligibility.ToDictionary(
            row => row.ReceiptHash,
            StringComparer.Ordinal);
        return AgentSignalSchoolCatalog.All
            .Select(lesson =>
            {
                var practice = receipts.Count(receipt =>
                    byHash[receipt.ReceiptHash].Class
                        == AgentQualificationClass.PracticeComplete
                    && string.Equals(
                        receipt.LessonOutcome?.LessonId,
                        lesson.Id,
                        StringComparison.Ordinal));
                var qualification = receipts.Count(receipt =>
                {
                    var row = byHash[receipt.ReceiptHash];
                    if (!row.Qualifying
                        || row.DeckKind != AgentDeckKind.QualificationTime)
                    {
                        return false;
                    }

                    return AgentQualificationCatalog.QualificationTimeLessons(lesson.Id)
                        .Any(seed =>
                            AgentQualificationCatalog.TryParseSeed(
                                receipt.GameplaySeed,
                                out var played)
                            && played == seed.GameplaySeed
                            && string.Equals(
                                seed.ModeId,
                                receipt.Division.ModeId,
                                StringComparison.Ordinal));
                });
                return new AgentGeneralizationRowV1(
                    AgentGeneralizationRowV1.Contract,
                    lesson.Id,
                    practice,
                    qualification,
                    qualification - practice);
            })
            .ToArray();
    }

    private static AgentStandingRowV1[] StandingsFrom(
        IReadOnlyList<AgentQualificationEligibilityV1> eligibility,
        IReadOnlyList<AgentExhibitionReceiptV2> receipts)
    {
        var byHash = receipts.ToDictionary(
            receipt => receipt.ReceiptHash,
            StringComparer.Ordinal);
        return eligibility
            .Where(row => row.Qualifying)
            .GroupBy(
                row => (
                    row.DivisionId,
                    row.PolicyVersion,
                    row.AgentId),
                comparer: Comparer())
            .Select(group =>
            {
                var best = group
                    .Select(row => byHash[row.ReceiptHash])
                    .OrderByDescending(receipt => receipt.Score)
                    .ThenByDescending(receipt => receipt.FinalTick)
                    .ThenBy(receipt => receipt.ReceiptHash, StringComparer.Ordinal)
                    .First();
                return new AgentStandingRowV1(
                    AgentStandingRowV1.Contract,
                    group.Key.DivisionId,
                    group.Key.PolicyVersion,
                    group.Key.AgentId,
                    group.Count(),
                    best.Score,
                    best.FinalTick,
                    best.ReceiptHash);
            })
            .OrderBy(row => row.DivisionId, StringComparer.Ordinal)
            .ThenBy(row => row.PolicyVersion, StringComparer.Ordinal)
            .ThenBy(row => row.AgentId, StringComparer.Ordinal)
            .ToArray();
    }

    private static AgentRivalBreakerRowV1[] RivalBreakersFrom(
        IReadOnlyList<AgentExhibitionReceiptV2> receipts)
    {
        return receipts
            .Where(receipt => receipt.RivalPersonalityId is not null)
            .Select(receipt =>
            {
                var kind = RivalBreakerKind(receipt);
                return new AgentRivalBreakerRowV1(
                    AgentRivalBreakerRowV1.Contract,
                    receipt.ReceiptHash,
                    receipt.Passport.AgentId,
                    receipt.RivalPersonalityId!,
                    kind,
                    receipt.Score,
                    receipt.RivalScore ?? 0,
                    receipt.StyleOutcome?.ContractId);
            })
            .ToArray();
    }

    private static EqualityComparer<(string DivisionId, string PolicyVersion, string AgentId)> Comparer() =>
        EqualityComparer<(string DivisionId, string PolicyVersion, string AgentId)>.Create(
            (left, right) =>
                string.Equals(left.DivisionId, right.DivisionId, StringComparison.Ordinal)
                && string.Equals(left.PolicyVersion, right.PolicyVersion, StringComparison.Ordinal)
                && string.Equals(left.AgentId, right.AgentId, StringComparison.Ordinal),
            value => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.DivisionId),
                StringComparer.Ordinal.GetHashCode(value.PolicyVersion),
                StringComparer.Ordinal.GetHashCode(value.AgentId)));
}
