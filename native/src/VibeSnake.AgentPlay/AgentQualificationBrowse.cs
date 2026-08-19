namespace VibeSnake.AgentPlay;

/// <summary>
/// One qualifying standing as a screen row. Rank is 1-based inside one
/// division and one policy version, so two policies never share a place.
/// </summary>
public sealed record AgentQualificationStandingViewV1(
    string Schema,
    int Rank,
    string DivisionId,
    string PolicyVersion,
    string AgentId,
    int QualifyingCount,
    int BestScore,
    int BestFinalTick,
    string BestReceiptHash,
    AgentQualificationClass BestClass,
    AgentDeckKind? BestDeckKind,
    AgentRivalBreakerKind RivalBreakerKind,
    string? RivalPersonalityId,
    int PracticeCompleteCount,
    int QualificationTimeCompleteCount,
    int GeneralizationGap)
{
    public const string Contract = "vibesnake-agent-qualification-standing-view-v1";
}

/// <summary>
/// One published division as a screen page. Empty pages stay listed so a
/// person can see that the other seven divisions were not mixed in.
/// </summary>
public sealed record AgentQualificationDivisionViewV1(
    string Schema,
    int Position,
    AgentDivisionManifestEntryV1 Division,
    int StandingCount,
    IReadOnlyList<AgentQualificationStandingViewV1> Standings)
{
    public const string Contract = "vibesnake-agent-qualification-division-view-v1";

    public bool IsEmpty => StandingCount == 0;
}

/// <summary>
/// The browse view over local qualification. Every ranking decision a screen
/// can make is already here: the eight published divisions, eligibility
/// counts that keep practice and voluntary finish off the list, score order
/// inside one division and policy, Rival Breaker on published terms, and the
/// practice-versus-qualification-time gap for the selected agent.
///
/// Building this never writes. The Godot screen renders it and does not
/// invent a grade, a mixed ranking, or a standing from a practice board.
/// </summary>
public sealed record AgentQualificationBrowseReportV1(
    string Schema,
    AgentQualificationReportV1 Report,
    int QualifyingCount,
    int PracticeCompleteCount,
    int PracticeIncompleteCount,
    int NonQualifyingFinishCount,
    int IneligibleCount,
    int RivalBrokenCount,
    int SelectedDivisionIndex,
    int SelectedStandingIndex,
    IReadOnlyList<AgentQualificationDivisionViewV1> Divisions)
{
    public const string Contract = "vibesnake-agent-qualification-browse-report-v1";

    public bool ArchiveIsEmpty => Report.Eligibility.Count == 0;

    public AgentQualificationDivisionViewV1 SelectedDivision =>
        Divisions[SelectedDivisionIndex];

    public AgentQualificationStandingViewV1? SelectedStanding =>
        SelectedStandingIndex >= 0
        && SelectedStandingIndex < SelectedDivision.Standings.Count
            ? SelectedDivision.Standings[SelectedStandingIndex]
            : null;

    /// <summary>
    /// The qualifying receipt a Confirm handoff should open in exhibitions,
    /// or null when this division has no standing.
    /// </summary>
    public string? HandoffReceiptHash => SelectedStanding?.BestReceiptHash;

    public static AgentQualificationBrowseReportV1 Create(
        IReadOnlyList<AgentArchivedExhibitionV2> entries,
        int selectedDivisionIndex = 0,
        int selectedStandingIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var report = AgentQualificationReportV1.FromArchive(entries);
        var receipts = entries.Select(entry => entry.Receipt).ToArray();
        var eligibilityByHash = report.Eligibility.ToDictionary(
            row => row.ReceiptHash,
            StringComparer.Ordinal);
        var breakerByHash = report.RivalBreakers.ToDictionary(
            row => row.ReceiptHash,
            StringComparer.Ordinal);
        var divisions = AgentQualificationCatalog.Manifest.Divisions
            .Select((division, position) =>
                DivisionView(
                    position,
                    division,
                    report.Standings,
                    receipts,
                    eligibilityByHash,
                    breakerByHash))
            .ToArray();
        var boundedDivision = Math.Clamp(selectedDivisionIndex, 0, divisions.Length - 1);
        if (selectedDivisionIndex == 0 && divisions[boundedDivision].StandingCount == 0)
        {
            var occupied = Array.FindIndex(divisions, page => page.StandingCount > 0);
            if (occupied >= 0)
            {
                boundedDivision = occupied;
            }
        }

        var standingCount = divisions[boundedDivision].StandingCount;
        var boundedStanding = standingCount == 0
            ? -1
            : Math.Clamp(selectedStandingIndex, 0, standingCount - 1);
        return new AgentQualificationBrowseReportV1(
            Contract,
            report,
            Count(report, AgentQualificationClass.QualifyingTerminal)
                + Count(report, AgentQualificationClass.QualifyingCapped),
            Count(report, AgentQualificationClass.PracticeComplete),
            Count(report, AgentQualificationClass.PracticeIncomplete),
            Count(report, AgentQualificationClass.NonQualifyingFinish),
            Count(report, AgentQualificationClass.Ineligible),
            report.RivalBreakers.Count(row => row.Kind == AgentRivalBreakerKind.Broken),
            boundedDivision,
            boundedStanding,
            divisions);
    }

    /// <summary>
    /// Moves the division page without wrapping, and lands on the first
    /// standing of that page so a person holding a direction never loops
    /// back through another division's ranking.
    /// </summary>
    public AgentQualificationBrowseReportV1 WithDivision(int index)
    {
        var bounded = Math.Clamp(index, 0, Divisions.Count - 1);
        var standingCount = Divisions[bounded].StandingCount;
        return this with
        {
            SelectedDivisionIndex = bounded,
            SelectedStandingIndex = standingCount == 0 ? -1 : 0,
        };
    }

    /// <summary>
    /// Moves the standing inside the current division without wrapping.
    /// </summary>
    public AgentQualificationBrowseReportV1 WithStanding(int index)
    {
        var standingCount = SelectedDivision.StandingCount;
        return this with
        {
            SelectedStandingIndex = standingCount == 0
                ? -1
                : Math.Clamp(index, 0, standingCount - 1),
        };
    }

    private static AgentQualificationDivisionViewV1 DivisionView(
        int position,
        AgentDivisionManifestEntryV1 division,
        IReadOnlyList<AgentStandingRowV1> standings,
        IReadOnlyList<AgentExhibitionReceiptV2> receipts,
        IReadOnlyDictionary<string, AgentQualificationEligibilityV1> eligibilityByHash,
        IReadOnlyDictionary<string, AgentRivalBreakerRowV1> breakerByHash)
    {
        var rows = standings
            .Where(row => string.Equals(
                row.DivisionId,
                division.DivisionId,
                StringComparison.Ordinal))
            .GroupBy(row => row.PolicyVersion, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderByDescending(row => row.BestScore)
                .ThenByDescending(row => row.BestFinalTick)
                .ThenBy(row => row.AgentId, StringComparer.Ordinal)
                .Select((row, rank) => StandingView(
                    rank + 1,
                    row,
                    receipts,
                    eligibilityByHash,
                    breakerByHash)))
            .ToArray();
        return new AgentQualificationDivisionViewV1(
            AgentQualificationDivisionViewV1.Contract,
            position,
            division,
            rows.Length,
            rows);
    }

    private static AgentQualificationStandingViewV1 StandingView(
        int rank,
        AgentStandingRowV1 row,
        IReadOnlyList<AgentExhibitionReceiptV2> receipts,
        IReadOnlyDictionary<string, AgentQualificationEligibilityV1> eligibilityByHash,
        IReadOnlyDictionary<string, AgentRivalBreakerRowV1> breakerByHash)
    {
        var bestEligibility = eligibilityByHash[row.BestReceiptHash];
        var agentRows = receipts
            .Where(receipt =>
                string.Equals(receipt.Passport.AgentId, row.AgentId, StringComparison.Ordinal)
                && string.Equals(
                    receipt.Passport.PolicyVersion,
                    row.PolicyVersion,
                    StringComparison.Ordinal)
                && string.Equals(
                    receipt.Division.DivisionId,
                    row.DivisionId,
                    StringComparison.Ordinal))
            .ToArray();
        var practice = agentRows.Count(receipt =>
            eligibilityByHash[receipt.ReceiptHash].Class
                == AgentQualificationClass.PracticeComplete);
        var qualificationTime = agentRows.Count(receipt =>
        {
            var eligibility = eligibilityByHash[receipt.ReceiptHash];
            if (!eligibility.Qualifying
                || eligibility.DeckKind != AgentDeckKind.QualificationTime
                || receipt.LessonOutcome is null)
            {
                return false;
            }

            return AgentQualificationCatalog.QualificationTimeLessons(
                    receipt.LessonOutcome.LessonId)
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
        var qualifyingHashes = agentRows
            .Where(receipt => eligibilityByHash[receipt.ReceiptHash].Qualifying)
            .Select(receipt => receipt.ReceiptHash)
            .ToArray();
        var broken = qualifyingHashes
            .Select(hash => breakerByHash.GetValueOrDefault(hash))
            .Any(breaker => breaker?.Kind == AgentRivalBreakerKind.Broken);
        var bestBreaker = breakerByHash.GetValueOrDefault(row.BestReceiptHash);
        var kind = broken
            ? AgentRivalBreakerKind.Broken
            : bestBreaker?.Kind ?? AgentRivalBreakerKind.NotARivalry;
        var rivalId = kind == AgentRivalBreakerKind.NotARivalry
            ? null
            : bestBreaker?.RivalPersonalityId
                ?? agentRows
                    .Select(receipt => receipt.RivalPersonalityId)
                    .FirstOrDefault(id => id is not null);
        return new AgentQualificationStandingViewV1(
            AgentQualificationStandingViewV1.Contract,
            rank,
            row.DivisionId,
            row.PolicyVersion,
            row.AgentId,
            row.QualifyingCount,
            row.BestScore,
            row.BestFinalTick,
            row.BestReceiptHash,
            bestEligibility.Class,
            bestEligibility.DeckKind,
            kind,
            rivalId,
            practice,
            qualificationTime,
            qualificationTime - practice);
    }

    private static int Count(
        AgentQualificationReportV1 report,
        AgentQualificationClass classKind) =>
        report.Eligibility.Count(row => row.Class == classKind);
}
