using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public sealed record PersonalityCatalogFileResult(
    string SourceName,
    string? PersonalityId,
    PersonalityLoadCode Code,
    string Message,
    string StatusLabel,
    IReadOnlyList<PersonalityValidationIssue> Issues)
{
    public bool IsValid => Code == PersonalityLoadCode.Success;
}

public sealed record PersonalityCatalogReport(
    IReadOnlyList<AiPersonalityProfile> BuiltIns,
    IReadOnlyList<AiPersonalityProfile> Customs,
    IReadOnlyList<PersonalityCatalogFileResult> Files,
    bool CapacityExceeded,
    bool Passed)
{
    public IReadOnlyList<AiPersonalityProfile> Available =>
        BuiltIns.Concat(Customs).ToArray();
}

/// <summary>
/// Bounded, read-only custom-personality discovery. Files are parsed as data and
/// never loaded as code. Invalid entries remain visible in the report.
/// </summary>
public static class PersonalityCatalogLoader
{
    public const int MaximumCustomFiles = 64;

    public static PersonalityCatalogReport LoadDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var builtIns = AiPersonalityCatalog.BuiltInProfiles;
        var custom = new List<AiPersonalityProfile>();
        var results = new List<PersonalityCatalogFileResult>();
        string[] files;
        try
        {
            if (!Directory.Exists(directory))
            {
                return new PersonalityCatalogReport(
                    builtIns,
                    custom,
                    results,
                    CapacityExceeded: false,
                    Passed: true);
            }

            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
            {
                results.Add(new PersonalityCatalogFileResult(
                    Path.GetFileName(directory),
                    null,
                    PersonalityLoadCode.PathUnsafe,
                    "Custom personality directory links are not loaded.",
                    "CUSTOM / INVALID",
                    Array.Empty<PersonalityValidationIssue>()));
                return new PersonalityCatalogReport(
                    builtIns,
                    custom,
                    results,
                    CapacityExceeded: false,
                    Passed: false);
            }

            files = Directory
                .GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(
                    Path.GetExtension(path),
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            results.Add(new PersonalityCatalogFileResult(
                Path.GetFileName(directory),
                null,
                PersonalityLoadCode.IoError,
                "Custom personality directory could not be read: " + exception.Message,
                "CUSTOM / INVALID",
                Array.Empty<PersonalityValidationIssue>()));
            return new PersonalityCatalogReport(
                builtIns,
                custom,
                results,
                CapacityExceeded: false,
                Passed: false);
        }

        var capacityExceeded = files.Length > MaximumCustomFiles;
        var acceptedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files.Take(MaximumCustomFiles))
        {
            var sourceName = Path.GetFileName(path);
            var id = Path.GetFileNameWithoutExtension(path);
            var read = PersonalityDocument.ReadFile(path);
            if (!read.IsSuccess || read.Document is null)
            {
                results.Add(ToFileResult(sourceName, id, read));
                continue;
            }

            var profileValidation = read.Document.ToProfile(id);
            if (!profileValidation.IsSuccess)
            {
                results.Add(ToFileResult(sourceName, id, profileValidation));
                continue;
            }

            if (!acceptedIds.Add(id))
            {
                results.Add(new PersonalityCatalogFileResult(
                    sourceName,
                    id,
                    PersonalityLoadCode.DuplicateId,
                    $"{sourceName}: custom personality ID duplicates another file.",
                    "CUSTOM / INVALID",
                    Array.Empty<PersonalityValidationIssue>()));
                continue;
            }

            custom.Add(read.Document.CreateProfile(id));
            results.Add(new PersonalityCatalogFileResult(
                sourceName,
                id,
                PersonalityLoadCode.Success,
                $"{sourceName}: custom personality is valid and unofficial.",
                AiPersonalityCatalog.CustomStatusLabel,
                Array.Empty<PersonalityValidationIssue>()));
        }

        foreach (var path in files.Skip(MaximumCustomFiles))
        {
            var sourceName = Path.GetFileName(path);
            results.Add(new PersonalityCatalogFileResult(
                sourceName,
                Path.GetFileNameWithoutExtension(path),
                PersonalityLoadCode.CapacityExceeded,
                $"{sourceName}: custom personality file limit is {MaximumCustomFiles}.",
                "CUSTOM / INVALID",
                Array.Empty<PersonalityValidationIssue>()));
        }

        return new PersonalityCatalogReport(
            builtIns,
            custom,
            results,
            capacityExceeded,
            Passed: results.All(result => result.IsValid));
    }

    private static PersonalityCatalogFileResult ToFileResult(
        string sourceName,
        string? id,
        PersonalityLoadResult result) =>
        new(
            sourceName,
            id,
            result.Code,
            result.Message.Contains(sourceName, StringComparison.Ordinal)
                ? result.Message
                : $"{sourceName}: {result.Message}",
            "CUSTOM / INVALID",
            result.Issues ?? Array.Empty<PersonalityValidationIssue>());
}
