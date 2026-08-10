using System.Text.Json;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class SpectatorLeagueDocumentTests
{
    [Fact]
    public void Defaults_are_closed_complete_and_round_trip_canonically()
    {
        var document = SpectatorLeagueDocument.CreateDefaults();
        var read = SpectatorLeagueDocument.Read(document.SerializeCanonical());

        Assert.True(read.IsSuccess, read.Message);
        Assert.Equal(10, read.Document!.Standings.Count);
        Assert.Equal(10, read.Document.Challenges.Count);
        Assert.Empty(read.Document.Rivalries);
        Assert.Equal(
            document.SerializeCanonical(),
            read.Document.SerializeCanonical());
        Assert.Equal(
            AiPersonalityCatalog.BuiltIn.Select(item => item.Id).Order(),
            read.Document.Standings.Select(item => item.PersonalityId));
    }

    [Fact]
    public void Match_updates_both_standings_rivalry_and_personality_milestones()
    {
        var result = MatchResult(
            featuredScore: 180,
            featuredTicks: 600,
            rivalScore: 90,
            rivalTicks: 450);
        var updated = SpectatorLeagueDocument.CreateDefaults().WithMatch(result);
        var featured = updated.StandingFor("balanced");
        var rival = updated.StandingFor("speed_demon");

        Assert.Equal(1, featured.Matches);
        Assert.Equal(1, featured.Wins);
        Assert.Equal(180, featured.AverageScore);
        Assert.Contains("first-broadcast", featured.MilestoneIds);
        Assert.Contains("match-win", featured.MilestoneIds);
        Assert.Contains("score-100", featured.MilestoneIds);
        Assert.Contains("survive-500", featured.MilestoneIds);
        Assert.Contains("combo-5", featured.MilestoneIds);
        Assert.Contains("power-route", featured.MilestoneIds);
        Assert.Contains("collision-save", featured.MilestoneIds);
        Assert.Equal(1, rival.Losses);
        var rivalry = Assert.Single(updated.Rivalries);
        Assert.Equal("balanced__vs__speed_demon", rivalry.Id);
        Assert.Equal(1, rivalry.LeftWins);
        Assert.Equal(0, rivalry.RightWins);
        Assert.Equal("balanced", updated.RankedStandings()[0].PersonalityId);

        var read = SpectatorLeagueDocument.Read(updated.SerializeCanonical());
        Assert.True(read.IsSuccess, read.Message);
        Assert.Equal(updated.SerializeCanonical(), read.Document!.SerializeCanonical());
    }

    [Fact]
    public void Equal_rules_human_challenge_updates_only_the_local_challenge_record()
    {
        var selection = SpectatorSelection.CreateDefault();
        var challenge = new SpectatorMatchSession(selection).CreateChallenge();
        var human = challenge.CreateHumanRun();
        for (var index = 0; index < 2_000 && human.Status == RunStatus.Running; index++)
        {
            human.Step();
        }

        Assert.NotEqual(RunStatus.Running, human.Status);
        var before = SpectatorLeagueDocument.CreateDefaults();
        var after = before.WithHumanChallenge(
            selection.PersonalityId,
            aiScore: human.Score + 10,
            challenge,
            human,
            ScoreRunContextCatalog.SeededChallenge);
        var record = after.Challenges.Single(item =>
            item.PersonalityId == selection.PersonalityId);

        Assert.Equal(1, record.Attempts);
        Assert.Equal(1, record.AiWins);
        Assert.Equal(human.Score, record.HumanBestScore);
        Assert.Equal(human.Score + 10, record.AiBestScore);
        Assert.Equal(before.Standings, after.Standings);
        Assert.Empty(after.Rivalries);
        Assert.Throws<ArgumentException>(() => before.WithHumanChallenge(
            selection.PersonalityId,
            0,
            challenge,
            human,
            ScoreRunContextCatalog.NormalHuman));
    }

    [Fact]
    public void Strict_reader_rejects_unknown_duplicate_future_oversized_and_forged_data()
    {
        var canonical = SpectatorLeagueDocument.CreateDefaults().SerializeCanonical();
        var unknown = canonical.Replace(
            "\"standings\":",
            "\"unknown\": true, \"standings\":",
            StringComparison.Ordinal);
        var duplicate = canonical.Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1, \"schemaVersion\": 1,",
            StringComparison.Ordinal);
        var future = canonical.Replace(
            "\"schemaVersion\": 1",
            "\"schemaVersion\": 2",
            StringComparison.Ordinal);

        Assert.Equal(
            SpectatorLeagueLoadCode.InvalidJson,
            SpectatorLeagueDocument.Read("{").Code);
        Assert.Equal(
            SpectatorLeagueLoadCode.InvalidField,
            SpectatorLeagueDocument.Read(unknown).Code);
        Assert.Equal(
            SpectatorLeagueLoadCode.InvalidField,
            SpectatorLeagueDocument.Read(duplicate).Code);
        Assert.Equal(
            SpectatorLeagueLoadCode.UnsupportedSchema,
            SpectatorLeagueDocument.Read(future).Code);
        Assert.Equal(
            SpectatorLeagueLoadCode.TooLarge,
            SpectatorLeagueDocument.Read(
                new string('x', SpectatorLeagueDocument.MaximumDocumentBytes + 1)).Code);
        Assert.Throws<ArgumentException>(() =>
            SpectatorLeagueDocument.CreateDefaults().WithMatch(
                MatchResult(10, 20, 5, 10) with { EqualRules = false }));
    }

    [Fact]
    public void Store_uses_absolute_atomic_path_and_leaves_no_temporary_files()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-spectator-league-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SpectatorLeagueStore(root);
            Assert.Equal(
                SpectatorLeagueDocument.CreateDefaults().SerializeCanonical(),
                store.Load().Document!.SerializeCanonical());

            var updated = SpectatorLeagueDocument.CreateDefaults().WithMatch(
                MatchResult(100, 500, 100, 500));
            store.Save(updated);
            store.Save(updated);
            var loaded = store.Load();

            Assert.True(loaded.IsSuccess, loaded.Message);
            Assert.Equal(updated.SerializeCanonical(), loaded.Document!.SerializeCanonical());
            Assert.True(Path.IsPathFullyQualified(store.LeaguePath));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp-*"));
            Assert.Throws<ArgumentException>(() => new SpectatorLeagueStore("relative"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reader_rejects_each_standing_and_challenge_bound_independently()
    {
        var document = SpectatorLeagueDocument.CreateDefaults();
        var standing = document.Standings[0];
        SpectatorStandingEntry[] invalidStandings =
        [
            standing with { Matches = -1 },
            standing with { Matches = SpectatorLeagueDocument.MaximumCounter + 1 },
            standing with { Wins = -1 },
            standing with { Losses = -1 },
            standing with { Ties = -1 },
            standing with { Wins = 1 },
            standing with { BestScore = -1 },
            standing with { BestScore = SnakeRun.MaximumScore + 1 },
            standing with { TotalScore = -1 },
            standing with
            {
                TotalScore = ((long)SnakeRun.MaximumScore
                    * SpectatorLeagueDocument.MaximumCounter) + 1,
            },
            standing with { BestSurvivalTicks = -1 },
            standing with
            {
                MilestoneIds =
                [
                    "first-broadcast",
                    "match-win",
                    "score-100",
                    "survive-500",
                    "combo-5",
                    "power-route",
                    "collision-save",
                    "unknown-eighth",
                ],
            },
            standing with { MilestoneIds = ["first-broadcast", "first-broadcast"] },
            standing with { MilestoneIds = ["unknown"] },
        ];
        foreach (var invalid in invalidStandings)
        {
            var standings = document.Standings.ToArray();
            standings[0] = invalid;
            Assert.Equal(
                SpectatorLeagueLoadCode.InvalidField,
                SpectatorLeagueDocument.Read(Raw(document with { Standings = standings })).Code);
        }

        var challenge = document.Challenges[0];
        SpectatorChallengeRecord[] invalidChallenges =
        [
            challenge with { Attempts = -1 },
            challenge with { Attempts = SpectatorLeagueDocument.MaximumCounter + 1 },
            challenge with { HumanWins = -1 },
            challenge with { AiWins = -1 },
            challenge with { Ties = -1 },
            challenge with { HumanWins = 1 },
            challenge with { HumanBestScore = -1 },
            challenge with { HumanBestScore = SnakeRun.MaximumScore + 1 },
            challenge with { AiBestScore = -1 },
            challenge with { AiBestScore = SnakeRun.MaximumScore + 1 },
        ];
        foreach (var invalid in invalidChallenges)
        {
            var challenges = document.Challenges.ToArray();
            challenges[0] = invalid;
            Assert.Equal(
                SpectatorLeagueLoadCode.InvalidField,
                SpectatorLeagueDocument.Read(Raw(document with { Challenges = challenges })).Code);
        }

        Assert.Equal(
            SpectatorLeagueLoadCode.InvalidField,
            SpectatorLeagueDocument.Read(Raw(document with { Standings = null! })).Code);
        Assert.Equal(
            SpectatorLeagueLoadCode.InvalidField,
            SpectatorLeagueDocument.Read(Raw(document with { Rivalries = null! })).Code);
        Assert.Equal(
            SpectatorLeagueLoadCode.InvalidField,
            SpectatorLeagueDocument.Read(Raw(document with { Challenges = null! })).Code);
        Assert.Equal(
            SpectatorLeagueLoadCode.InvalidField,
            SpectatorLeagueDocument.Read(Raw(document with
            {
                Standings = document.Standings.Skip(1).ToArray(),
            })).Code);
        Assert.Equal(
            SpectatorLeagueLoadCode.InvalidField,
            SpectatorLeagueDocument.Read(Raw(document with
            {
                Challenges = document.Challenges.Skip(1).ToArray(),
            })).Code);
    }

    [Fact]
    public void Reader_rejects_each_rivalry_integrity_mutation()
    {
        var document = SpectatorLeagueDocument.CreateDefaults();
        var valid = new SpectatorRivalryRecord(
            "balanced__vs__speed_demon",
            "balanced",
            "speed_demon",
            Matches: 1,
            LeftWins: 1,
            RightWins: 0,
            Ties: 0,
            LeftBestScore: 10,
            RightBestScore: 5);
        IReadOnlyList<SpectatorRivalryRecord>[] invalidRivalries =
        [
            [valid with { LeftPersonalityId = "missing" }],
            [valid with { RightPersonalityId = "missing" }],
            [valid with
            {
                LeftPersonalityId = "speed_demon",
                RightPersonalityId = "balanced",
            }],
            [valid with { Id = "wrong" }],
            [valid, valid],
            [valid with { Matches = -1 }],
            [valid with { Matches = SpectatorLeagueDocument.MaximumCounter + 1 }],
            [valid with { LeftWins = -1 }],
            [valid with { RightWins = -1 }],
            [valid with { Ties = -1 }],
            [valid with { Matches = 2 }],
            [valid with { LeftBestScore = -1 }],
            [valid with { LeftBestScore = SnakeRun.MaximumScore + 1 }],
            [valid with { RightBestScore = -1 }],
            [valid with { RightBestScore = SnakeRun.MaximumScore + 1 }],
        ];
        foreach (var invalid in invalidRivalries)
        {
            Assert.Equal(
                SpectatorLeagueLoadCode.InvalidField,
                SpectatorLeagueDocument.Read(Raw(document with { Rivalries = invalid })).Code);
        }
    }

    [Fact]
    public void Match_validation_rejects_each_identity_and_outcome_mutation()
    {
        var valid = MatchResult(100, 500, 90, 450);
        SpectatorMatchResult[] invalidMatches =
        [
            valid with
            {
                Rival = valid.Rival with { PersonalityId = valid.Featured.PersonalityId },
            },
            valid with { ConfigHash = "short" },
            valid with { ConfigHash = new string('G', 64) },
            valid with { ConfigHash = new string('g', 64) },
            valid with { ConfigHash = new string('0', 64) },
            valid with { EqualRules = false },
            valid with { AiProgressionAwarded = true },
            valid with
            {
                Featured = valid.Featured with
                {
                    Status = RunStatus.Running,
                    DeathCause = DeathCause.None,
                },
            },
            valid with
            {
                Rival = valid.Rival with
                {
                    Status = RunStatus.Running,
                    DeathCause = DeathCause.None,
                },
            },
            valid with { PredictionCorrect = true },
        ];
        foreach (var invalid in invalidMatches)
        {
            Assert.Throws<ArgumentException>(() =>
                SpectatorLeagueDocument.CreateDefaults().WithMatch(invalid));
        }

        SpectatorLaneOutcome[] invalidOutcomes =
        [
            valid.Featured with { Score = -1 },
            valid.Featured with { Score = SnakeRun.MaximumScore + 1 },
            valid.Featured with { FinalTick = -1 },
            valid.Featured with { MaximumCombo = -1 },
            valid.Featured with { FoodEaten = -1 },
            valid.Featured with { PowerCollections = -1 },
            valid.Featured with { CollisionRecoveries = -1 },
            valid.Featured with { EndedByBroadcastLimit = true },
            valid.Featured with { FinalStateHash = "short" },
            valid.Featured with { FinalStateHash = "G123456789abcdef" },
            valid.Featured with { FinalStateHash = "g123456789abcdef" },
        ];
        foreach (var invalid in invalidOutcomes)
        {
            Assert.Throws<ArgumentException>(() =>
                SpectatorLeagueDocument.CreateDefaults().WithMatch(valid with
                {
                    Featured = invalid,
                }));
        }

        var capped = valid with
        {
            Featured = valid.Featured with
            {
                Status = RunStatus.Running,
                DeathCause = DeathCause.None,
                EndedByBroadcastLimit = true,
            },
            Rival = valid.Rival with
            {
                Status = RunStatus.Running,
                DeathCause = DeathCause.None,
                EndedByBroadcastLimit = true,
            },
        };
        Assert.Single(SpectatorLeagueDocument.CreateDefaults().WithMatch(capped).Rivalries);
    }

    [Fact]
    public void Rivalry_updates_cover_reverse_existing_and_tied_results()
    {
        var first = MatchResult(10, 20, 5, 10);
        var reverse = first with
        {
            Featured = first.Rival with { Score = 20, FinalTick = 30 },
            Rival = first.Featured with { Score = 10, FinalTick = 20 },
        };
        var tied = first with
        {
            Featured = first.Featured with { Score = 10, FinalTick = 20 },
            Rival = first.Rival with { Score = 10, FinalTick = 20 },
        };
        var document = SpectatorLeagueDocument.CreateDefaults()
            .WithMatch(first)
            .WithMatch(reverse)
            .WithMatch(tied);
        var rivalry = Assert.Single(document.Rivalries);

        Assert.Equal(3, rivalry.Matches);
        Assert.Equal(1, rivalry.LeftWins);
        Assert.Equal(1, rivalry.RightWins);
        Assert.Equal(1, rivalry.Ties);
    }

    [Fact]
    public void Human_challenge_and_store_boundaries_fail_closed()
    {
        var selection = SpectatorSelection.CreateDefault();
        var challenge = new SpectatorMatchSession(selection).CreateChallenge();
        var running = challenge.CreateHumanRun();
        var defaults = SpectatorLeagueDocument.CreateDefaults();
        Assert.Throws<ArgumentException>(() => defaults.WithHumanChallenge(
            selection.PersonalityId,
            0,
            challenge,
            running,
            ScoreRunContextCatalog.SeededChallenge));

        while (running.Status == RunStatus.Running)
        {
            running.Step();
        }
        Assert.Throws<ArgumentException>(() => defaults.WithHumanChallenge(
            selection.PersonalityId,
            -1,
            challenge,
            running,
            ScoreRunContextCatalog.SeededChallenge));
        Assert.Throws<ArgumentException>(() => defaults.WithHumanChallenge(
            selection.PersonalityId,
            SnakeRun.MaximumScore + 1,
            challenge,
            running,
            ScoreRunContextCatalog.SeededChallenge));
        Assert.Throws<ArgumentException>(() => defaults.WithHumanChallenge(
            "missing",
            0,
            challenge,
            running,
            ScoreRunContextCatalog.SeededChallenge));

        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-spectator-league-io-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SpectatorLeagueStore(root);
            using (var locked = new FileStream(
                store.LeaguePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                Assert.Equal(SpectatorLeagueLoadCode.IoError, store.Load().Code);
            }

            File.Delete(store.LeaguePath);
            Directory.CreateDirectory(store.LeaguePath);
            var saveFailure = Record.Exception(() => store.Save(defaults));
            Assert.True(saveFailure is IOException or UnauthorizedAccessException);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp-*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SpectatorMatchResult MatchResult(
        int featuredScore,
        int featuredTicks,
        int rivalScore,
        int rivalTicks)
    {
        var selection = SpectatorSelection.CreateDefault();
        var config = selection.CreateRunConfig();
        var featured = new SpectatorLaneOutcome(
            "balanced",
            featuredScore,
            featuredTicks,
            RunStatus.Dead,
            DeathCause.Starvation,
            MaximumCombo: 5,
            FoodEaten: 10,
            PowerCollections: 1,
            CollisionRecoveries: 1,
            FinalStateHash: "0123456789abcdef",
            EndedByBroadcastLimit: false);
        var rival = new SpectatorLaneOutcome(
            "speed_demon",
            rivalScore,
            rivalTicks,
            RunStatus.Dead,
            DeathCause.SelfCollision,
            MaximumCombo: 2,
            FoodEaten: 4,
            PowerCollections: 0,
            CollisionRecoveries: 0,
            FinalStateHash: "fedcba9876543210",
            EndedByBroadcastLimit: false);
        return new SpectatorMatchResult(
            selection.GameplaySeed,
            selection.ModeId,
            selection.ModeVersion,
            config.ComputeConfigHash(),
            featured,
            rival,
            SpectatorPredictionKind.None,
            PredictionCorrect: null,
            EqualRules: true,
            AiProgressionAwarded: false);
    }

    private static string Raw(SpectatorLeagueDocument document) =>
        JsonSerializer.Serialize(
            document,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
}
