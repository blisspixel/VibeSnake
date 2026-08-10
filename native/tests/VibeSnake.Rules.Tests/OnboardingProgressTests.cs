using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class OnboardingProgressTests
{
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
