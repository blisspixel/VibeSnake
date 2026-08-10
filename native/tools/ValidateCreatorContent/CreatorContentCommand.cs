using System.Text;
using System.Text.Json;
using VibeSnake.Persistence;

namespace ValidateCreatorContent;

public sealed record CreatorValidationIssue(string Field, string Message, string? Received);

public sealed record CreatorPackValidation(
    string Id,
    string SourceName,
    string Kind,
    string Code,
    string Message);

public sealed record CreatorValidationReport(
    int SchemaVersion,
    string Contract,
    string Kind,
    bool Passed,
    string Code,
    string Message,
    bool ExecutesContent,
    bool ArbitraryCodeSupported,
    string? ContentId,
    IReadOnlyList<CreatorValidationIssue> Issues,
    IReadOnlyList<CreatorPackValidation> Packs,
    IReadOnlyList<string> ResolutionOrder);

public static class CreatorContentCommand
{
    public const int SchemaVersion = 1;
    public const string Contract = "creator-content-validation-v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private sealed record LoadedManifest(
        string SourceName,
        ContentPackManifest Manifest);

    public static int Run(
        IReadOnlyList<string>? arguments,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        if (arguments is null || arguments.Count == 0)
        {
            WriteUsage(standardError);
            return 2;
        }

        return arguments[0] switch
        {
            "personality" => ValidatePersonality(arguments, standardOutput, standardError),
            "pack-set" => ValidatePackSet(arguments, standardOutput, standardError),
            _ => InvalidCommand(standardError),
        };
    }

    private static int ValidatePersonality(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error)
    {
        if (arguments.Count != 2
            && (arguments.Count != 4 || arguments[2] != "--id"))
        {
            WriteUsage(error);
            return 2;
        }

        var path = arguments[1];
        var sourceName = SafeFileName(path);
        var personalityId = arguments.Count == 4
            ? arguments[3]
            : Path.GetFileNameWithoutExtension(sourceName);
        PersonalityLoadResult result;
        try
        {
            result = PersonalityDocument.ReadFile(path);
            if (result.IsSuccess && result.Document is not null)
            {
                result = result.Document.ToProfile(personalityId);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            result = new PersonalityLoadResult(
                PersonalityLoadCode.PathUnsafe,
                "Personality path is invalid.");
        }

        var success = result.IsSuccess;
        var issues = (result.Issues ?? Array.Empty<PersonalityValidationIssue>())
            .Select(issue => new CreatorValidationIssue(
                issue.Field,
                SingleLine(issue.Message),
                issue.Received is null ? null : SingleLine(issue.Received)))
            .ToArray();
        var message = result.Code == PersonalityLoadCode.IoError
            ? sourceName + ": personality file could not be read."
            : SingleLine(result.Message);
        var report = Report(
            "personality",
            success,
            "personality-" + JsonNamingPolicy.KebabCaseLower.ConvertName(result.Code.ToString()),
            message,
            success ? personalityId : null,
            issues,
            [],
            []);
        WriteReport(output, report);
        return success ? 0 : 1;
    }

    private static int ValidatePackSet(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error)
    {
        if (arguments.Count < 6
            || !int.TryParse(arguments[4], out var rulesVersion)
            || rulesVersion <= 0)
        {
            WriteUsage(error);
            return 2;
        }

        try
        {
            var inventory = ContentInventory.LoadFromFile(arguments[1]);
            var manifests = arguments
                .Skip(5)
                .Select(path => new LoadedManifest(
                    SafeFileName(path),
                    ContentPackManifest.CheckCanonicalFile(path, inventory)))
                .ToArray();
            var core = manifests[0];
            if (core.Manifest.Kind != ContentPackKind.Core)
            {
                return WritePackFailure(
                    output,
                    "core-kind-required",
                    "The first manifest must be the offline core pack.",
                    manifests);
            }

            if (manifests.Skip(1).Any(item => item.Manifest.Kind != ContentPackKind.Radio))
            {
                return WritePackFailure(
                    output,
                    "optional-kind-invalid",
                    "Every manifest after the core must be an optional radio pack.",
                    manifests);
            }

            var duplicateId = manifests
                .GroupBy(item => item.Manifest.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicateId is not null)
            {
                return WritePackFailure(
                    output,
                    "pack-id-collision",
                    "Pack IDs never override each other; duplicate ID rejected: " + duplicateId,
                    manifests);
            }

            var installed = manifests.ToDictionary(
                item => item.Manifest.Id,
                item => item.Manifest.Version,
                StringComparer.Ordinal);
            var evaluations = manifests
                .Select(item => new
                {
                    Item = item,
                    Compatibility = ContentPackResolver.Evaluate(
                        item.Manifest,
                        arguments[2],
                        arguments[3],
                        rulesVersion,
                        installed),
                })
                .ToArray();
            var packs = evaluations
                .Select(item => Pack(
                    item.Item.Manifest,
                    item.Item.SourceName,
                    item.Compatibility.Code,
                    item.Compatibility.Message))
                .ToArray();
            var passed = evaluations.All(item => item.Compatibility.Compatible);
            var order = new[] { core.Manifest.Id }
                .Concat(manifests
                    .Skip(1)
                    .Select(item => item.Manifest.Id)
                    .OrderBy(id => id, StringComparer.Ordinal))
                .ToArray();
            var report = Report(
                "pack-set",
                passed,
                passed ? "pack-set-valid" : "pack-set-incompatible",
                passed
                    ? "Core and optional radio manifests are canonical, collision-free, and compatible."
                    : "One or more manifests are incompatible; no pack set is accepted.",
                core.Manifest.Id,
                [],
                packs,
                order);
            WriteReport(output, report);
            return passed ? 0 : 1;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException
                or ArgumentException
                or NotSupportedException)
        {
            var report = Report(
                "pack-set",
                false,
                "pack-set-invalid",
                BoundMessage(exception.Message),
                null,
                [],
                [],
                []);
            WriteReport(output, report);
            return 1;
        }
    }

    private static int WritePackFailure(
        TextWriter output,
        string code,
        string message,
        IReadOnlyList<LoadedManifest> manifests)
    {
        var packs = manifests
            .Select(item => Pack(
                item.Manifest,
                item.SourceName,
                code,
                message))
            .ToArray();
        WriteReport(output, Report("pack-set", false, code, message, null, [], packs, []));
        return 1;
    }

    private static CreatorPackValidation Pack(
        ContentPackManifest manifest,
        string sourceName,
        string code,
        string message) =>
        new(
            manifest.Id,
            sourceName,
            manifest.Kind == ContentPackKind.Core ? "core" : "radio",
            code,
            SingleLine(message));

    private static CreatorValidationReport Report(
        string kind,
        bool passed,
        string code,
        string message,
        string? contentId,
        IReadOnlyList<CreatorValidationIssue> issues,
        IReadOnlyList<CreatorPackValidation> packs,
        IReadOnlyList<string> resolutionOrder) =>
        new(
            SchemaVersion,
            Contract,
            kind,
            passed,
            code,
            SingleLine(message),
            ExecutesContent: false,
            ArbitraryCodeSupported: false,
            contentId,
            issues,
            packs,
            resolutionOrder);

    private static void WriteReport(TextWriter output, CreatorValidationReport report)
    {
        output.Write(JsonSerializer.Serialize(report, SerializerOptions));
        output.Write('\n');
    }

    private static int InvalidCommand(TextWriter error)
    {
        WriteUsage(error);
        return 2;
    }

    private static void WriteUsage(TextWriter writer) =>
        writer.WriteLine(
            "Usage:\n"
            + "  ValidateCreatorContent personality <file.json> [--id <custom-id>]\n"
            + "  ValidateCreatorContent pack-set <inventory.json> <game-version> "
            + "<ruleset-id> <rules-version> <core.json> [radio.json ...]");

    private static string SafeFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return "content.json";
        }
    }

    private static string BoundMessage(string value)
    {
        const int maximumLength = 512;
        var singleLine = SingleLine(value);
        return singleLine.Length <= maximumLength
            ? singleLine
            : singleLine[..maximumLength];
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
