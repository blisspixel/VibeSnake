using System.Text;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ProfileScoreStoreWriteTests
{
    public static IEnumerable<object[]> UnsafeMetadata()
    {
        string[] metadata =
        [
            "\"schemaVersion\":99",
            "\"schema_version\":99",
            "\"schemaVersion\":0",
            "\"schema_version\":-1",
            "\"schemaVersion\":\"1\"",
            "\"schema_version\":null",
            "\"schemaVersion\":1.5",
            "\"schemaVersion\":2147483648",
            "\"schemaVersion\":1,\"schema_version\":2",
            "\"schemaVersion\":1,\"schemaVersion\":1",
            "\"schema_version\":1,\"schema_version\":1",
        ];
        foreach (var store in new[] { "personal-best", "score-history", "spectator-league" })
        {
            foreach (var declaration in metadata)
            {
                yield return [store, "{" + declaration + "}"];
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnsafeMetadata))]
    public void Save_preserves_unsupported_or_ambiguous_metadata(string kind, string contents)
    {
        WithRoot(root =>
        {
            var operations = new ObservingWriteOperations();
            var store = CreateStore(kind, root, operations);
            File.WriteAllText(store.Path, contents);
            var original = File.ReadAllBytes(store.Path);

            Assert.Throws<InvalidOperationException>(store.Save);

            Assert.Equal(original, File.ReadAllBytes(store.Path));
            Assert.Empty(operations.StagedPaths);
            Assert.Equal(0, operations.MoveCount);
        });
    }

    [Theory]
    [InlineData("personal-best")]
    [InlineData("score-history")]
    [InlineData("spectator-league")]
    public void Save_rechecks_future_file_published_after_supported_load(string kind)
    {
        WithRoot(root =>
        {
            var store = CreateStore(kind, root);
            store.Save();
            Assert.True(store.LoadSucceeded());
            const string future = "{\"schemaVersion\":99,\"retain\":\"newer profile\"}";
            File.WriteAllText(store.Path, future);

            Assert.Throws<InvalidOperationException>(store.Save);

            Assert.Equal(future, File.ReadAllText(store.Path));
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        });
    }

    [Theory]
    [InlineData("personal-best", "schemaVersion")]
    [InlineData("personal-best", "schema_version")]
    [InlineData("score-history", "schemaVersion")]
    [InlineData("score-history", "schema_version")]
    [InlineData("spectator-league", "schemaVersion")]
    [InlineData("spectator-league", "schema_version")]
    public void Save_preserves_future_file_published_while_staging(string kind, string schemaField)
    {
        WithRoot(root =>
        {
            StoreHarness? store = null;
            var future = "{\"" + schemaField + "\":99,\"retain\":true}";
            var operations = new ObservingWriteOperations(afterWrite: _ =>
                File.WriteAllText(store!.Path, future));
            store = CreateStore(kind, root, operations);
            CreateStore(kind, root).Save();
            Assert.True(store.LoadSucceeded());

            Assert.Throws<InvalidOperationException>(store.Save);

            Assert.Equal(future, File.ReadAllText(store.Path));
            Assert.Single(operations.StagedPaths);
            Assert.Equal(0, operations.MoveCount);
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        });
    }

    [Theory]
    [InlineData("personal-best", false)]
    [InlineData("personal-best", true)]
    [InlineData("score-history", false)]
    [InlineData("score-history", true)]
    [InlineData("spectator-league", false)]
    [InlineData("spectator-league", true)]
    public void Failed_save_preserves_document_and_other_writers_stage(string kind, bool failMove)
    {
        WithRoot(root =>
        {
            var existing = CreateStore(kind, root);
            existing.Save();
            var original = File.ReadAllBytes(existing.Path);
            var competingStage = existing.Path + ".tmp-other-writer";
            File.WriteAllText(competingStage, "other writer owns this");
            var operations = new ObservingWriteOperations(failWrite: !failMove, failMove: failMove);
            var store = CreateStore(kind, root, operations);

            Assert.Throws<IOException>(store.Save);

            Assert.Equal(original, File.ReadAllBytes(existing.Path));
            Assert.Equal("other writer owns this", File.ReadAllText(competingStage));
            Assert.Single(operations.StagedPaths);
            Assert.False(File.Exists(operations.StagedPaths[0]));
            Assert.Equal([competingStage], Directory.GetFiles(root, "*.tmp-*"));
        });
    }

    [Theory]
    [InlineData("personal-best")]
    [InlineData("score-history")]
    [InlineData("spectator-league")]
    public void Interleaved_writers_use_independent_stages(string kind)
    {
        WithRoot(root =>
        {
            var secondOperations = new ObservingWriteOperations();
            var second = CreateStore(kind, root, secondOperations);
            var firstOperations = new ObservingWriteOperations(afterWrite: firstStage =>
            {
                var stagedBytes = File.ReadAllBytes(firstStage);
                second.Save();
                Assert.True(File.Exists(firstStage));
                Assert.Equal(stagedBytes, File.ReadAllBytes(firstStage));
            });
            var first = CreateStore(kind, root, firstOperations);

            first.Save();

            Assert.Single(firstOperations.StagedPaths);
            Assert.Single(secondOperations.StagedPaths);
            Assert.NotEqual(firstOperations.StagedPaths[0], secondOperations.StagedPaths[0]);
            Assert.Equal(1, firstOperations.MoveCount);
            Assert.Equal(1, secondOperations.MoveCount);
            Assert.True(first.LoadSucceeded());
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        });
    }

    [Theory]
    [InlineData("personal-best", "{")]
    [InlineData("personal-best", "{\"schema_version\":1}")]
    [InlineData("personal-best", "{\"schemaVersion\":1,\"schema_version\":1}")]
    [InlineData("score-history", "{")]
    [InlineData("score-history", "{\"schema_version\":1}")]
    [InlineData("score-history", "{\"schemaVersion\":1,\"schema_version\":1}")]
    [InlineData("spectator-league", "{")]
    [InlineData("spectator-league", "{\"schema_version\":1}")]
    [InlineData("spectator-league", "{\"schemaVersion\":1,\"schema_version\":1}")]
    public void Supported_metadata_and_explicit_malformed_recovery_can_save(string kind, string contents)
    {
        WithRoot(root =>
        {
            var store = CreateStore(kind, root);
            File.WriteAllText(store.Path, contents);

            store.Save();

            Assert.True(store.LoadSucceeded());
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        });
    }

    [Theory]
    [InlineData("personal-best")]
    [InlineData("score-history")]
    [InlineData("spectator-league")]
    public void Injected_constructor_rejects_missing_operations(string kind)
    {
        WithRoot(root =>
        {
            Assert.Throws<ArgumentNullException>(() => CreateStore(kind, root, null, inject: true));
        });
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("schema_version")]
    public void Legacy_import_reports_changed_destination_without_changing_source(string schemaField)
    {
        WithRoot(root =>
        {
            ScoreHistoryStore? store = null;
            var future = "{\"" + schemaField + "\":99,\"retain\":true}";
            var operations = new ObservingWriteOperations(afterWrite: _ =>
                File.WriteAllText(store!.ScoreHistoryPath, future));
            store = new ScoreHistoryStore(root, operations);
            new ScoreHistoryStore(root).Save(ScoreHistoryDocument.CreateDefaults());
            store.EnsurePythonImportInbox();
            const string source = "{\"schema_version\":1,\"migrations\":{\"legacy_highscore_json\":true},\"scores\":[]}";
            File.WriteAllText(store.PythonImportInboxPath, source);
            var sourceBefore = File.ReadAllBytes(store.PythonImportInboxPath);

            var imported = store.ImportPythonTopTen();

            Assert.Equal(PythonScoreImportCode.DestinationBlocked, imported.Code);
            Assert.False(imported.IsSuccess);
            Assert.Equal(future, File.ReadAllText(store.ScoreHistoryPath));
            Assert.Equal(sourceBefore, File.ReadAllBytes(store.PythonImportInboxPath));
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        });
    }

    private static StoreHarness CreateStore(
        string kind,
        string root,
        IPreferencesWriteOperations? operations = null,
        bool inject = false)
    {
        switch (kind)
        {
            case "personal-best":
                var personalBest = inject || operations is not null
                    ? new PersonalBestStore(root, operations!)
                    : new PersonalBestStore(root);
                return new StoreHarness(
                    personalBest.PersonalBestPath,
                    () => personalBest.Save(PersonalBestDocument.CreateDefaults()),
                    () => personalBest.Load().IsSuccess);
            case "score-history":
                var scoreHistory = inject || operations is not null
                    ? new ScoreHistoryStore(root, operations!)
                    : new ScoreHistoryStore(root);
                return new StoreHarness(
                    scoreHistory.ScoreHistoryPath,
                    () => scoreHistory.Save(ScoreHistoryDocument.CreateDefaults()),
                    () => scoreHistory.Load().IsSuccess);
            case "spectator-league":
                var spectatorLeague = inject || operations is not null
                    ? new SpectatorLeagueStore(root, operations!)
                    : new SpectatorLeagueStore(root);
                return new StoreHarness(
                    spectatorLeague.LeaguePath,
                    () => spectatorLeague.Save(SpectatorLeagueDocument.CreateDefaults()),
                    () => spectatorLeague.Load().IsSuccess);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void WithRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-score-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            test(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record StoreHarness(string Path, Action Save, Func<bool> LoadSucceeded);

    private sealed class ObservingWriteOperations(
        Action<string>? afterWrite = null,
        bool failWrite = false,
        bool failMove = false) : IPreferencesWriteOperations
    {
        public List<string> StagedPaths { get; } = [];

        public int MoveCount { get; private set; }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteAllText(string path, string contents, Encoding encoding)
        {
            StagedPaths.Add(path);
            File.WriteAllText(path, failWrite ? contents[..(contents.Length / 2)] : contents, encoding);
            if (failWrite)
            {
                throw new IOException("Injected partial write failure.");
            }

            afterWrite?.Invoke(path);
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            if (failMove)
            {
                throw new IOException("Injected replacement failure.");
            }

            MoveCount++;
            File.Move(sourcePath, destinationPath, overwrite);
        }

        public void Delete(string path) => File.Delete(path);
    }
}
