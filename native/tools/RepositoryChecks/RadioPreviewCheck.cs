namespace RepositoryChecks;

internal static class RadioPreviewCheck
{
    internal const string PublicAssetsMessage = "radio review must not use the public assets tree";

    internal const string ArchiveMessage = "radio review must use the ignored archive";

    internal const string DirectoryMessage = "radio preview directory must be a regular directory";

    internal static readonly PreviewSample[] Catalog =
    [
        new("The Flow Signal", "flow_signal_crystalline_frequency.mp3"),
        new("Chaos Theory", "jazz_attractor_coil.mp3"),
        new("The Global Coil", "global_coil_dancehall_fang.mp3"),
        new("Ourotron", "synthwave_cipher_molt.mp3"),
        new("The Pit", "dance_glide_algorithm.mp3"),
        new("The Bureau", "the_bureau_bebop_bulletin.mp3"),
        new("The Strike", "rock_clockwork_venom.mp3"),
        new("Underground Scales", "underground_scales_baile_beats.mp3"),
    ];

    internal static string RequireDirectory(string repositoryRoot, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var full = Path.GetFullPath(directory);
        if (IsReparse(full) || (Exists(full) && !IsDirectory(full)))
        {
            throw new RadioPreviewException(DirectoryMessage);
        }

        var normalized = Path.TrimEndingDirectorySeparator(full);
        if (IsInside(Path.Combine(root, "assets"), normalized))
        {
            throw new RadioPreviewException(PublicAssetsMessage);
        }

        if (IsInside(root, normalized) && !IsInside(Path.Combine(root, "archive"), normalized))
        {
            throw new RadioPreviewException(ArchiveMessage);
        }

        return normalized;
    }

    internal static List<AvailableSample> FindSamples(string directory)
    {
        var found = new List<AvailableSample>(Catalog.Length);
        foreach (var sample in Catalog)
        {
            var path = Path.Combine(directory, sample.FileName);
            if (!File.Exists(path) || IsReparse(path))
            {
                continue;
            }

            long length;
            try
            {
                length = new FileInfo(path).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new RadioPreviewException(
                    "radio preview sample is unreadable: "
                    + sample.FileName
                    + ": "
                    + SingleLine(exception));
            }

            if (length > 0)
            {
                found.Add(new AvailableSample(sample.Station, sample.FileName, path));
            }
        }

        return found;
    }

    internal static string MissingMessage(string directory) =>
        "No configured samples are available under " + directory;

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool IsDirectory(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.Directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RadioPreviewException("radio preview path is unreadable: " + SingleLine(exception));
        }
    }

    private static bool IsReparse(string path)
    {
        if (!Exists(path))
        {
            return false;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RadioPreviewException("radio preview path is unreadable: " + SingleLine(exception));
        }
    }

    private static bool IsInside(string parent, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (string.Equals(root, full, comparison))
        {
            return true;
        }

        return full.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static string SingleLine(Exception exception)
    {
        var text = exception.Message.Replace("\r", " ", StringComparison.Ordinal);
        text = text.Replace("\n", " ", StringComparison.Ordinal);
        return text.Trim();
    }

    internal readonly record struct PreviewSample(string Station, string FileName);

    internal readonly record struct AvailableSample(string Station, string FileName, string FullPath);
}

internal sealed class RadioPreviewException : InvalidOperationException
{
    internal RadioPreviewException(string message)
        : base(message)
    {
    }
}
