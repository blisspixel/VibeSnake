using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ContentCreditsDocumentTests
{
    [Fact]
    public void Renders_deterministic_manifest_bound_credits_in_stable_order()
    {
        var core = Manifest(
            ContentPackManifest.CorePackId,
            ContentPackKind.Core,
            "Vibe Snake Core",
            [
                Credit("z-credit", "Project source", "Apache-2.0", "Core team", "review z"),
                Credit("a-credit", "Project source\r\ncurated", "Apache-2.0", "Core team", "review a"),
            ],
            [
                File("asset:z", "z/file.bin", "z-credit"),
                File("asset:b", "b/file.bin", "a-credit"),
                File("asset:a", "a/file.bin", "a-credit"),
            ]);
        var radio = Manifest(
            "vibesnake.radio.flow-signal",
            ContentPackKind.Radio,
            "The Flow Signal",
            [Credit("radio-credit", "Original soundtrack", "Apache-2.0", "Contributors", "reviewed")],
            [File("asset:track", "audio/track.mp3", "radio-credit")]);

        var first = ContentCreditsDocument.Render([radio, core]);
        var second = ContentCreditsDocument.Render([core, radio]);

        Assert.Equal(first, second);
        Assert.StartsWith("# Vibe Snake Content Credits and Third-Party Notices\n", first);
        Assert.Contains("Document contract: `content-credits-v1`", first, StringComparison.Ordinal);
        Assert.True(
            first.IndexOf("## Vibe Snake Core", StringComparison.Ordinal)
            < first.IndexOf("## The Flow Signal", StringComparison.Ordinal));
        Assert.True(
            first.IndexOf("### Credit `a-credit`", StringComparison.Ordinal)
            < first.IndexOf("### Credit `z-credit`", StringComparison.Ordinal));
        Assert.True(
            first.IndexOf("`a/file.bin`", StringComparison.Ordinal)
            < first.IndexOf("`b/file.bin`", StringComparison.Ordinal));
        Assert.Contains("Source: Project source  curated", first, StringComparison.Ordinal);
        Assert.Contains("Kind: Core", first, StringComparison.Ordinal);
        Assert.Contains("Kind: Optional radio", first, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', first);
        Assert.DoesNotContain("C:\\", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_invalid_manifest_sets_and_forged_credit_references()
    {
        Assert.Throws<ArgumentNullException>(() => ContentCreditsDocument.Render(null!));
        Assert.Throws<ArgumentException>(() => ContentCreditsDocument.Render([]));
        Assert.Throws<ArgumentException>(() => ContentCreditsDocument.Render(
            Enumerable.Repeat(
                    Manifest(
                        ContentPackManifest.CorePackId,
                        ContentPackKind.Core,
                        "Core",
                        [Credit("credit", "source", "license", "attribution", "review")],
                        [File("asset:file", "file.bin", "credit")]),
                    ContentCreditsDocument.MaximumPackCount + 1)
                .ToArray()));
        Assert.Throws<ArgumentException>(() => ContentCreditsDocument.Render(
            [(ContentPackManifest)null!]));

        var core = Manifest(
            ContentPackManifest.CorePackId,
            ContentPackKind.Core,
            "Core",
            [Credit("credit", "source", "license", "attribution", "review")],
            [File("asset:file", "file.bin", "credit")]);
        var radio = Manifest(
            "vibesnake.radio.test",
            ContentPackKind.Radio,
            "Radio",
            [Credit("credit", "source", "license", "attribution", "review")],
            [File("asset:radio", "radio.mp3", "credit")]);
        Assert.Throws<InvalidDataException>(() => ContentCreditsDocument.Render([core, core]));
        Assert.Throws<InvalidDataException>(() => ContentCreditsDocument.Render([radio]));
        Assert.Throws<InvalidDataException>(() => ContentCreditsDocument.Render(
            [core, core with { Id = "vibesnake.second-core" }]));

        var forged = core with
        {
            Files = [File("asset:file", "file.bin", "missing-credit")],
        };
        Assert.Throws<InvalidDataException>(() => ContentCreditsDocument.Render([forged]));
    }

    [Fact]
    public void Rejects_a_generated_document_beyond_the_output_bound()
    {
        var core = Manifest(
            ContentPackManifest.CorePackId,
            ContentPackKind.Core,
            "Core",
            [Credit("credit", "source", "license", "attribution", "review")],
            [File("asset:file", "file.bin", "credit")]) with
        {
            Description = new string('x', ContentCreditsDocument.MaximumDocumentCharacters),
        };

        Assert.Throws<InvalidDataException>(() => ContentCreditsDocument.Render([core]));
    }

    private static ContentPackManifest Manifest(
        string id,
        ContentPackKind kind,
        string displayName,
        IReadOnlyList<ContentPackCredit> credits,
        IReadOnlyList<ContentPackFile> files) => new(
            ContentPackManifest.CurrentSchemaVersion,
            id,
            "1.0.0",
            kind,
            displayName,
            "Curated content.",
            new ContentPackCompatibility(
                new ContentPackVersionRange("1.0.0", "2.0.0"),
                new ContentPackRulesetRange("vibesnake-core", 4, 5)),
            new ContentPackInventoryBinding(1, "assets", new string('a', 64)),
            [],
            files,
            credits,
            null);

    private static ContentPackCredit Credit(
        string id,
        string source,
        string license,
        string attribution,
        string review) =>
        new(id, source, license, attribution, review);

    private static ContentPackFile File(string id, string path, string creditId) =>
        new(
            id,
            path,
            "application/octet-stream",
            10,
            new string('b', 64),
            "runtime",
            "required",
            creditId);
}
