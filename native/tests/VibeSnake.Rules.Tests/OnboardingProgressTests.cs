using System.Text;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class OnboardingProgressTests
{
    [Theory]
    [InlineData("\"tutorialRevision\":99")]
    [InlineData("\"tutorialRevision\":99,\"tutorialRevision\":1")]
    [InlineData("\"tutorialRevision\":1,\"tutorialRevision\":99")]
    [InlineData("\"tutorialRevision\":1,\"tutorialRevision\":1")]
    [InlineData("\"tutorialRevision\":\"future\"")]
    [InlineData("\"tutorialRevision\":2147483648")]
    [InlineData("\"tutorialRevision\":0")]
    public void Saves_preserve_unsupported_or_ambiguous_tutorial_revisions(string declarations)
    {
        var root = CreateRoot();
        try
        {
            var store = new OnboardingStore(root);
            var bytes = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"status\":\"completed\"," + declarations + "}\n");
            File.WriteAllBytes(store.OnboardingPath, bytes);
            _ = store.Load();
            Assert.Throws<InvalidOperationException>(() => store.Save(OnboardingProgressDocument.CreateDefaults()));
            Assert.Equal(bytes, File.ReadAllBytes(store.OnboardingPath));
            Assert.Empty(Directory.GetFiles(root, "onboarding.json.tmp*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Future_tutorial_revision_load_and_publication_during_staging_are_preserved()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, OnboardingProgressDocument.FileName);
            var future = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"status\":\"completed\",\"tutorialRevision\":99,\"futureData\":[1,2]}\n");
            var store = new OnboardingStore(root);
            File.WriteAllBytes(path, future);
            Assert.Equal(OnboardingLoadCode.InvalidField, store.Load().Code);
            Assert.Throws<InvalidOperationException>(() => store.Save(OnboardingProgressDocument.CreateDefaults()));
            Assert.Equal(future, File.ReadAllBytes(path));

            File.WriteAllText(path, OnboardingProgressDocument.CreateDefaults().SerializeCanonical());
            var operations = new TestWriteOperations(afterWrite: _ => File.WriteAllBytes(path, future));
            var stagedStore = new OnboardingStore(root, operations);
            var loaded = stagedStore.Load();
            Assert.True(loaded.IsSuccess);
            Assert.Throws<InvalidOperationException>(() => stagedStore.Save(
                loaded.Document!.WithStatus(OnboardingStatus.Skipped)));
            Assert.Equal(future, File.ReadAllBytes(path));
            Assert.False(operations.MoveCalled);
            Assert.Single(operations.StagedPaths);
            Assert.False(File.Exists(operations.StagedPaths[0]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"schemaVersion\":99")]
    [InlineData("\"schema_version\":99")]
    [InlineData("\"schemaVersion\":1,\"schema_version\":99")]
    [InlineData("\"schema_version\":99,\"schemaVersion\":1")]
    [InlineData("\"schemaVersion\":99,\"schemaVersion\":1")]
    [InlineData("\"schemaVersion\":1,\"schemaVersion\":99")]
    [InlineData("\"schemaVersion\":1,\"schemaVersion\":1")]
    [InlineData("\"schemaVersion\":\"future\"")]
    [InlineData("\"schemaVersion\":2147483648")]
    [InlineData("\"schemaVersion\":0")]
    public void Saves_preserve_every_unsupported_or_ambiguous_schema_declaration(string declarations)
    {
        var root = CreateRoot();
        try
        {
            var store = new OnboardingStore(root);
            var bytes = Encoding.UTF8.GetBytes("{" + declarations + ",\"futureData\":[1,2,3]}\n");
            File.WriteAllBytes(store.OnboardingPath, bytes);
            _ = store.Load();
            Assert.Throws<InvalidOperationException>(() => store.Save(
                OnboardingProgressDocument.CreateDefaults().WithStatus(OnboardingStatus.Completed)));
            Assert.Equal(bytes, File.ReadAllBytes(store.OnboardingPath));
            Assert.Empty(Directory.GetFiles(root, "onboarding.json.tmp*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Future_progress_published_after_supported_load_during_staging_is_preserved()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, OnboardingProgressDocument.FileName);
            File.WriteAllText(path, OnboardingProgressDocument.CreateDefaults().SerializeCanonical());
            var future = Encoding.UTF8.GetBytes("{\"schemaVersion\":99,\"futureStatus\":\"preserve\"}\n");
            var operations = new TestWriteOperations(afterWrite: _ => File.WriteAllBytes(path, future));
            var store = new OnboardingStore(root, operations);
            var loaded = store.Load();
            Assert.True(loaded.IsSuccess);
            Assert.Throws<InvalidOperationException>(() => store.Save(
                loaded.Document!.WithStatus(OnboardingStatus.Skipped)));
            Assert.Equal(future, File.ReadAllBytes(path));
            Assert.False(operations.MoveCalled);
            Assert.Single(operations.StagedPaths);
            Assert.False(File.Exists(operations.StagedPaths[0]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Future_schema_load_never_allows_a_default_save_to_replace_original_bytes()
    {
        var root = CreateRoot();
        try
        {
            var store = new OnboardingStore(root);
            var bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":99,\"status\":\"future-choice\"}\n");
            File.WriteAllBytes(store.OnboardingPath, bytes);
            Assert.Equal(OnboardingLoadCode.UnsupportedSchema, store.Load().Code);
            Assert.Throws<InvalidOperationException>(() => store.Save(OnboardingProgressDocument.CreateDefaults()));
            Assert.Equal(bytes, File.ReadAllBytes(store.OnboardingPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Staging_write_failure_cleans_partial_owned_file_and_preserves_existing_progress()
    {
        var root = CreateRoot();
        try
        {
            var store = new OnboardingStore(root);
            store.Save(OnboardingProgressDocument.CreateDefaults());
            var bytes = File.ReadAllBytes(store.OnboardingPath);
            var operations = new TestWriteOperations(afterWrite: _ => throw new IOException("Injected staging failure."));
            var failingStore = new OnboardingStore(root, operations);
            Assert.Throws<IOException>(() => failingStore.Save(
                OnboardingProgressDocument.CreateDefaults().WithStatus(OnboardingStatus.Completed)));
            Assert.Equal(bytes, File.ReadAllBytes(store.OnboardingPath));
            Assert.False(operations.MoveCalled);
            Assert.Single(operations.StagedPaths);
            Assert.False(File.Exists(operations.StagedPaths[0]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Success_and_failed_replacement_clean_only_owned_unique_staging(bool failMove)
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, OnboardingProgressDocument.FileName);
            var legacyTemporary = path + ".tmp";
            var unrelatedTemporary = path + ".tmp-other-writer";
            File.WriteAllText(legacyTemporary, "legacy pending update");
            File.WriteAllText(unrelatedTemporary, "other pending update");
            File.WriteAllText(path, OnboardingProgressDocument.CreateDefaults().SerializeCanonical());
            var operations = new TestWriteOperations(failMove: failMove);
            var store = new OnboardingStore(root, operations);
            var progress = OnboardingProgressDocument.CreateDefaults().WithStatus(OnboardingStatus.Completed);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (failMove)
                {
                    Assert.Throws<IOException>(() => store.Save(progress));
                }
                else
                {
                    store.Save(progress);
                }
            }

            Assert.Equal(2, operations.StagedPaths.Distinct(StringComparer.Ordinal).Count());
            Assert.All(operations.StagedPaths, temporary => Assert.False(File.Exists(temporary)));
            Assert.Equal("legacy pending update", File.ReadAllText(legacyTemporary));
            Assert.Equal("other pending update", File.ReadAllText(unrelatedTemporary));
            Assert.Equal(failMove ? OnboardingStatus.NotStarted : OnboardingStatus.Completed,
                store.Load().Document!.Status);
            if (!failMove)
            {
                Assert.Equal(Encoding.UTF8.GetBytes(progress.SerializeCanonical()), File.ReadAllBytes(path));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Explicit_save_can_recover_malformed_progress_without_schema_declarations(string malformed)
    {
        var root = CreateRoot();
        try
        {
            var store = new OnboardingStore(root);
            File.WriteAllText(store.OnboardingPath, malformed);
            Assert.False(store.Load().IsSuccess);
            store.Save(OnboardingProgressDocument.CreateDefaults().WithStatus(OnboardingStatus.Completed));
            Assert.Equal(OnboardingStatus.Completed, store.Load().Document!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestWriteOperations(
        Action<string>? afterWrite = null,
        bool failMove = false) : IPreferencesWriteOperations
    {
        public List<string> StagedPaths { get; } = [];

        public bool MoveCalled { get; private set; }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteAllText(string path, string contents, Encoding encoding)
        {
            StagedPaths.Add(path);
            PhysicalPreferencesWriteOperations.Instance.WriteAllText(path, contents, encoding);
            afterWrite?.Invoke(path);
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            MoveCalled = true;
            if (failMove)
            {
                throw new IOException("Injected replacement failure.");
            }

            File.Move(sourcePath, destinationPath, overwrite);
        }

        public void Delete(string path) => File.Delete(path);
    }

    [Fact]
    public void Canonical_statuses_round_trip()
    {
        foreach (var status in Enum.GetValues<OnboardingStatus>())
        {
            var document = OnboardingProgressDocument.CreateDefaults().WithStatus(status);
            var read = OnboardingProgressDocument.Read(document.SerializeCanonical());

            Assert.True(read.IsSuccess);
            Assert.False(read.IsNewProfile);
            Assert.Equal(status, read.Document!.Status);
            Assert.Equal(document.SerializeCanonical(), read.Document.SerializeCanonical());
        }
    }

    [Fact]
    public void Missing_store_file_is_the_only_new_profile_signal()
    {
        var root = CreateRoot();
        try
        {
            var store = new OnboardingStore(root);
            var missing = store.Load();
            Assert.True(missing.IsSuccess);
            Assert.True(missing.IsNewProfile);
            Assert.Equal(OnboardingStatus.NotStarted, missing.Document!.Status);

            store.Save(missing.Document);
            var persisted = store.Load();
            Assert.True(persisted.IsSuccess);
            Assert.False(persisted.IsNewProfile);
            Assert.Equal(OnboardingStatus.NotStarted, persisted.Document!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Atomic_store_overwrites_only_the_onboarding_document()
    {
        var root = CreateRoot();
        try
        {
            var sentinel = Path.Combine(root, "profile.sentinel");
            File.WriteAllText(sentinel, "preserve");
            var store = new OnboardingStore(root);
            store.Save(
                OnboardingProgressDocument.CreateDefaults()
                    .WithStatus(OnboardingStatus.Skipped));
            store.Save(
                OnboardingProgressDocument.CreateDefaults()
                    .WithStatus(OnboardingStatus.Completed));

            Assert.Equal("preserve", File.ReadAllText(sentinel));
            Assert.False(File.Exists(store.OnboardingPath + ".tmp"));
            Assert.Equal(OnboardingStatus.Completed, store.Load().Document!.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_invalid_documents_without_returning_progress()
    {
        string[] invalidPayloads =
        [
            "",
            "{",
            "[]",
            "{}",
            """{"schemaVersion":"1","status":"not-started","tutorialRevision":1}""",
            """{"schemaVersion":0,"status":"not-started","tutorialRevision":1}""",
            """{"schemaVersion":2,"status":"not-started","tutorialRevision":1}""",
            """{"schemaVersion":1,"tutorialRevision":1}""",
            """{"schemaVersion":1,"status":0,"tutorialRevision":1}""",
            """{"schemaVersion":1,"status":"unknown","tutorialRevision":1}""",
            """{"schemaVersion":1,"status":"completed"}""",
            """{"schemaVersion":1,"status":"completed","tutorialRevision":"1"}""",
            """{"schemaVersion":1,"status":"completed","tutorialRevision":2}""",
        ];

        foreach (var payload in invalidPayloads)
        {
            var result = OnboardingProgressDocument.Read(payload);
            Assert.False(result.IsSuccess, payload);
            Assert.Null(result.Document);
        }
    }

    [Fact]
    public void Rejects_noncanonical_objects_and_invalid_roots()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OnboardingProgressDocument.CreateDefaults()
                .WithStatus((OnboardingStatus)byte.MaxValue));
        Assert.Throws<InvalidDataException>(
            () => (OnboardingProgressDocument.CreateDefaults() with
            {
                SchemaVersion = 2,
            }).SerializeCanonical());
        Assert.Throws<InvalidDataException>(
            () => (OnboardingProgressDocument.CreateDefaults() with
            {
                TutorialRevision = 2,
            }).SerializeCanonical());
        Assert.Throws<InvalidDataException>(
            () => (OnboardingProgressDocument.CreateDefaults() with
            {
                Status = (OnboardingStatus)byte.MaxValue,
            }).SerializeCanonical());
        Assert.Throws<ArgumentException>(() => new OnboardingStore("relative/path"));
        Assert.Throws<ArgumentException>(() => new OnboardingStore(" "));
    }

    [Fact]
    public void Store_reports_read_io_failure_without_overwrite()
    {
        var root = CreateRoot();
        try
        {
            var store = new OnboardingStore(root);
            File.WriteAllText(store.OnboardingPath, "locked");
            using var locked = new FileStream(
                store.OnboardingPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            var result = store.Load();
            Assert.Equal(OnboardingLoadCode.IoError, result.Code);
            Assert.False(result.IsSuccess);
            Assert.Contains("could not be read", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Constants_are_stable()
    {
        Assert.Equal(1, OnboardingProgressDocument.CurrentSchemaVersion);
        Assert.Equal(1, OnboardingProgressDocument.CurrentTutorialRevision);
        Assert.Equal("onboarding.json", OnboardingProgressDocument.FileName);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-onboarding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
