using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

/// <summary>
/// Which published deck a seed belongs to. Practice seeds teach; qualification-time
/// seeds ask whether the same facts still hold on a different board. Neither is
/// secret. AA-05's checked-in non-practice fixtures are the qualification-time
/// lesson deck, not a withheld exam.
/// </summary>
public enum AgentDeckKind : byte
{
    Practice = 0,
    QualificationTime = 1,
}

/// <summary>
/// How one verified exhibition may be used for local qualification. Voluntary
/// <c>finish_match</c> on a running exhibition is never a qualifying result.
/// A completed Signal School practice is practice evidence, not a standing.
/// </summary>
public enum AgentQualificationClass : byte
{
    Ineligible = 0,
    PracticeComplete = 1,
    PracticeIncomplete = 2,
    NonQualifyingFinish = 3,
    QualifyingTerminal = 4,
    QualifyingCapped = 5,
}

/// <summary>
/// Whether a qualifying rivalry beat a named rival on that rival's published
/// characteristic terms. Ahead on score alone is not Rival Breaker when the
/// rival has a mapped Style Contract.
/// </summary>
public enum AgentRivalBreakerKind : byte
{
    NotARivalry = 0,
    NonQualifying = 1,
    Behind = 2,
    Level = 3,
    BeatScore = 4,
    Broken = 5,
}

/// <summary>
/// One closed competition division this build will rank. Results from different
/// modes, seed visibilities, observation profiles, or action profiles never
/// share a standing. The seed is deliberately omitted: a division names the
/// ruleset, not the board.
/// </summary>
public sealed record AgentDivisionManifestEntryV1(
    string Schema,
    string DivisionId,
    string ModeId,
    int ModeVersion,
    AgentSeedVisibility SeedVisibility,
    string ObservationProfile,
    string ActionProfile)
{
    public const string Contract = "vibesnake-agent-division-manifest-entry-v1";
}

/// <summary>
/// The immutable list of divisions this build publishes. It is a catalog, not a
/// ranking: adding a division later is a contract change, not a migration of
/// anyone's history.
/// </summary>
public sealed record AgentDivisionManifestV1(
    string Schema,
    string CatalogId,
    IReadOnlyList<AgentDivisionManifestEntryV1> Divisions,
    string ManifestHash)
{
    public const string Contract = "vibesnake-agent-division-manifest-v1";
    public const string CatalogIdValue = "vibesnake-agent-division-catalog-v1";
}

/// <summary>
/// One published seed a person or agent can actually play. Practice seeds are
/// the canonical Signal School boards. Qualification-time seeds are the
/// already-public non-practice evaluator boards plus a small closed set of
/// style and rivalry boards.
/// </summary>
public sealed record AgentDeckSeedV1(
    string Schema,
    AgentDeckKind DeckKind,
    string ModeId,
    AgentSeedVisibility SeedVisibility,
    ulong GameplaySeed,
    int MaximumSteps,
    string? LessonId,
    string? StyleContractId,
    string? RivalPersonalityId)
{
    public const string Contract = "vibesnake-agent-deck-seed-v1";
}

/// <summary>
/// The public practice and qualification-time decks. They are published with
/// the host so a later ranking cannot pretend the boards were secret.
/// </summary>
public sealed record AgentQualificationDecksV1(
    string Schema,
    IReadOnlyList<AgentDeckSeedV1> Practice,
    IReadOnlyList<AgentDeckSeedV1> QualificationTime)
{
    public const string Contract = "vibesnake-agent-qualification-decks-v1";
}

/// <summary>
/// What "the rival's characteristic terms" means for Rival Breaker. A mapped
/// Style Contract is the characteristic; rivals without one are beaten on
/// equal-seed score alone.
/// </summary>
public sealed record AgentRivalBreakerTermsV1(
    string Schema,
    string RivalPersonalityId,
    string CharacteristicMeaning,
    string? StyleContractId)
{
    public const string Contract = "vibesnake-agent-rival-breaker-terms-v1";
}

/// <summary>
/// Closed qualification catalogs. Nothing here is inferred from a live match:
/// divisions, decks, and Rival Breaker terms are the same for every caller.
/// </summary>
public static class AgentQualificationCatalog
{
    public static AgentDivisionManifestV1 Manifest { get; } = BuildManifest();

    public static AgentQualificationDecksV1 Decks { get; } = BuildDecks();

    public static IReadOnlyList<AgentRivalBreakerTermsV1> RivalBreakerTerms { get; } =
        BuildRivalBreakerTerms();

    public static bool IsPublishedDivision(string divisionId) =>
        Manifest.Divisions.Any(entry =>
            string.Equals(entry.DivisionId, divisionId, StringComparison.Ordinal));

    public static AgentDeckSeedV1? FindPracticeLesson(string lessonId) =>
        Decks.Practice.FirstOrDefault(seed =>
            string.Equals(seed.LessonId, lessonId, StringComparison.Ordinal));

    public static IReadOnlyList<AgentDeckSeedV1> QualificationTimeLessons(string lessonId) =>
        Decks.QualificationTime
            .Where(seed => string.Equals(seed.LessonId, lessonId, StringComparison.Ordinal))
            .ToArray();

    public static AgentDeckKind? DeckKindOf(AgentExhibitionReceiptV2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!TryParseSeed(receipt.GameplaySeed, out var seed))
        {
            return null;
        }

        if (MatchesDeck(Decks.Practice, receipt, seed))
        {
            return AgentDeckKind.Practice;
        }

        if (MatchesDeck(Decks.QualificationTime, receipt, seed))
        {
            return AgentDeckKind.QualificationTime;
        }

        return null;
    }

    public static AgentRivalBreakerTermsV1 TermsFor(string rivalPersonalityId) =>
        RivalBreakerTerms.SingleOrDefault(terms =>
            string.Equals(terms.RivalPersonalityId, rivalPersonalityId, StringComparison.Ordinal))
        ?? throw new ArgumentException(
            $"Unknown built-in rival {rivalPersonalityId}.",
            nameof(rivalPersonalityId));

    private static AgentDivisionManifestV1 BuildManifest()
    {
        var visibilities = new[] { AgentSeedVisibility.Open, AgentSeedVisibility.Blind };
        var profiles = new[]
        {
            AgentPassportV4.FourDirectionActionProfile,
            AgentPassportV4.FourDirectionBurstActionProfile,
        };
        var entries = new List<AgentDivisionManifestEntryV1>();
        foreach (var mode in RunModeCatalog.All)
        {
            foreach (var visibility in visibilities)
            {
                foreach (var actionProfile in profiles)
                {
                    entries.Add(new AgentDivisionManifestEntryV1(
                        AgentDivisionManifestEntryV1.Contract,
                        AgentDivisionIdentityV1.ComposeDivisionId(
                            mode.Id,
                            mode.Version,
                            visibility,
                            AgentPassportV4.SymbolicStepObservationProfile,
                            actionProfile),
                        mode.Id,
                        mode.Version,
                        visibility,
                        AgentPassportV4.SymbolicStepObservationProfile,
                        actionProfile));
                }
            }
        }

        var ordered = entries
            .OrderBy(entry => entry.DivisionId, StringComparer.Ordinal)
            .ToArray();
        var canonical = string.Join(
            "\n",
            ordered.Select(entry => entry.DivisionId));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            "vibesnake-agent-division-catalog-v1\n" + canonical)));
        return new AgentDivisionManifestV1(
            AgentDivisionManifestV1.Contract,
            AgentDivisionManifestV1.CatalogIdValue,
            Array.AsReadOnly(ordered),
            hash);
    }

    private static AgentQualificationDecksV1 BuildDecks()
    {
        var practice = AgentSignalSchoolCatalog.All
            .Select(lesson => LessonSeed(AgentDeckKind.Practice, lesson, lesson.PracticeSeed))
            .ToArray();
        AgentDeckSeedV1[] qualification =
        [
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId), 1UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId), 2UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.WrapLineId), 1UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.WrapLineId), 2UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.HungerRouteId), 1UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.HungerRouteId), 2UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.ExitRouteId), 1UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.ExitRouteId), 2UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.PowerRouteId), 3UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.PowerRouteId), 2UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.RecoverRouteId), 5UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.RecoverRouteId), 6UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.ComboRouteId), 1UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.ComboRouteId), 2UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.DeathReadId), 1UL),
            LessonSeed(AgentDeckKind.QualificationTime, AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.DeathReadId), 2UL),
            new(
                AgentDeckSeedV1.Contract,
                AgentDeckKind.QualificationTime,
                RunModeCatalog.ClassicId,
                AgentSeedVisibility.Open,
                42UL,
                200,
                LessonId: null,
                AgentStyleContractCatalog.StillwaterId,
                RivalPersonalityId: null),
            new(
                AgentDeckSeedV1.Contract,
                AgentDeckKind.QualificationTime,
                RunModeCatalog.ClassicId,
                AgentSeedVisibility.Open,
                91UL,
                200,
                LessonId: null,
                StyleContractId: null,
                "optimal"),
        ];
        return new AgentQualificationDecksV1(
            AgentQualificationDecksV1.Contract,
            Array.AsReadOnly(practice),
            Array.AsReadOnly(qualification));
    }

    private static AgentDeckSeedV1 LessonSeed(
        AgentDeckKind kind,
        AgentSignalLessonDefinitionV2 lesson,
        ulong seed) =>
        new(
            AgentDeckSeedV1.Contract,
            kind,
            lesson.ModeId,
            AgentSeedVisibility.Open,
            seed,
            lesson.MaximumSteps,
            lesson.Id,
            StyleContractId: null,
            RivalPersonalityId: null);

    private static AgentRivalBreakerTermsV1[] BuildRivalBreakerTerms() =>
        AiPersonalityCatalog.BuiltIn
            .Select(personality =>
            {
                var style = personality.Id switch
                {
                    "zen_master" => AgentStyleContractCatalog.StillwaterId,
                    "greedy" => AgentStyleContractCatalog.CrownchaserId,
                    "yolo" => AgentStyleContractCatalog.EdgeProphetId,
                    "power_hunter" => AgentStyleContractCatalog.MutagenistId,
                    "speed_demon" => AgentStyleContractCatalog.RedlineId,
                    _ => null,
                };
                var claim = AiPersonalityCatalog.BehaviorClaims.Single(item =>
                    string.Equals(item.PersonalityId, personality.Id, StringComparison.Ordinal));
                return new AgentRivalBreakerTermsV1(
                    AgentRivalBreakerTermsV1.Contract,
                    personality.Id,
                    claim.PlayerFacingMeaning,
                    style);
            })
            .ToArray();

    private static bool MatchesDeck(
        IReadOnlyList<AgentDeckSeedV1> deck,
        AgentExhibitionReceiptV2 receipt,
        ulong seed)
    {
        foreach (var item in deck)
        {
            if (item.GameplaySeed != seed
                || item.SeedVisibility != receipt.Division.SeedVisibility
                || !string.Equals(item.ModeId, receipt.Division.ModeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (item.LessonId is { } lesson)
            {
                if (string.Equals(
                        receipt.LessonOutcome?.LessonId,
                        lesson,
                        StringComparison.Ordinal)
                    || receipt.LessonOutcome is null
                        && receipt.StyleOutcome is null
                        && receipt.RivalPersonalityId is null)
                {
                    return true;
                }

                continue;
            }

            if (item.StyleContractId is { } style
                && string.Equals(
                    receipt.StyleOutcome?.ContractId,
                    style,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (item.RivalPersonalityId is { } rival
                && string.Equals(
                    receipt.RivalPersonalityId,
                    rival,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryParseSeed(string gameplaySeed, out ulong seed) =>
        ulong.TryParse(
            gameplaySeed,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out seed);
}
