using System.Text.Json;

namespace RepositoryChecks;

internal static class FixedCanonicalFixtureFile
{
    internal const int MaximumBytes = 64 * 1024;
    internal const int MaximumSiblingEntries = 256;

    private const int MaximumDiagnosticCodeUnits = 512;

    private static readonly Type[] ExpectedFailureTypes =
    [
        typeof(ArgumentException),
        typeof(IOException),
        typeof(UnauthorizedAccessException),
        typeof(InvalidDataException),
        typeof(NotSupportedException),
    ];

    internal static byte[] Read(
        string repositoryRoot,
        string relativePath,
        string label)
    {
        var root = ResolveRepositoryRoot(repositoryRoot);
        var parts = ValidateRelativePath(relativePath);
        var path = ResolveExistingPath(root, parts, relativePath, label);
        return ReadBounded(path, label);
    }

    internal static void Write(
        string repositoryRoot,
        string relativePath,
        string label,
        ReadOnlySpan<byte> bytes)
    {
        EnsureBounded(bytes.Length, label);
        var root = ResolveRepositoryRoot(repositoryRoot);
        var parts = ValidateRelativePath(relativePath);
        var path = ResolveWritablePath(root, parts, label);
        WriteAtomic(root, parts, path, label, bytes);
    }

    internal static bool IsExpectedFailure(Exception exception) =>
        ExpectedFailureTypes.Any(type => type.IsAssignableFrom(exception.GetType()));

    internal static string SingleLine(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Length <= MaximumDiagnosticCodeUnits)
        {
            return singleLine;
        }

        var length = MaximumDiagnosticCodeUnits;
        if (char.IsHighSurrogate(singleLine[length - 1])
            && char.IsLowSurrogate(singleLine[length]))
        {
            length--;
        }

        return singleLine[..length];
    }

    internal static void EnsureBounded(int byteCount, string label)
    {
        if (byteCount > MaximumBytes)
        {
            throw new InvalidDataException($"{label} exceeds {MaximumBytes} bytes");
        }
    }

    internal static void RunCleanup(
        Exception? primaryFailure,
        Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        try
        {
            cleanup();
        }
        catch when (primaryFailure is not null)
        {
            // Preserve the replacement failure rather than masking it with cleanup.
        }
    }

    private static string ResolveRepositoryRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new InvalidDataException("repository root is missing or is not a directory");
        }

        RejectLink(root, "repository root");
        return root;
    }

    private static string[] ValidateRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\'))
        {
            throw new InvalidDataException("fixture path must be a portable relative path");
        }

        var parts = relativePath.Split('/');
        if (parts.Length < 2
            || parts.Any(part =>
                part.Length == 0
                || part is "." or ".."
                || !string.Equals(Path.GetFileName(part), part, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("fixture path must contain only portable segments");
        }

        return parts;
    }

    private static string ResolveExistingPath(
        string root,
        string[] parts,
        string relativePath,
        string label)
    {
        var current = root;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            CountSiblingsAndRejectPortableAlias(current, part, label);
            current = Path.Combine(current, part);
            if (!Directory.Exists(current))
            {
                throw new InvalidDataException(
                    $"{label} parent is missing or is not a directory");
            }

            RejectLink(current, $"{label} parent");
        }

        CountSiblingsAndRejectPortableAlias(current, parts[^1], label);
        var path = Path.Combine(current, parts[^1]);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"required fixture is missing: {relativePath}");
        }

        RejectLink(path, label);
        return path;
    }

    private static string ResolveWritablePath(
        string root,
        string[] parts,
        string label,
        string? ignoredEntry = null)
    {
        var current = root;
        foreach (var part in parts.Take(parts.Length - 1))
        {
            var siblingCount = CountSiblingsAndRejectPortableAlias(
                current,
                part,
                label,
                ignoredEntry);
            current = Path.Combine(current, part);
            if (TryGetAttributes(current, out var attributes))
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"{label} parent must not be a link");
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    throw new InvalidDataException($"{label} parent is not a directory");
                }
            }
            else
            {
                EnsureSiblingCapacity(siblingCount, 1, label);
                Directory.CreateDirectory(current);
                RejectLink(current, $"{label} parent");
            }
        }

        var outputSiblingCount = CountSiblingsAndRejectPortableAlias(
            current,
            parts[^1],
            label,
            ignoredEntry);
        var path = Path.Combine(current, parts[^1]);
        if (TryGetAttributes(path, out var fixtureAttributes))
        {
            if ((fixtureAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"{label} must not be a link");
            }

            if ((fixtureAttributes & FileAttributes.Directory) != 0)
            {
                throw new InvalidDataException($"{label} path is a directory");
            }
        }

        // The temporary output needs one same-directory entry until its rename.
        EnsureSiblingCapacity(outputSiblingCount, 1, label);
        return path;
    }

    private static int CountSiblingsAndRejectPortableAlias(
        string parent,
        string expectedName,
        string label,
        string? ignoredEntry = null)
    {
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
        {
            if (ignoredEntry is not null
                && string.Equals(entry, ignoredEntry, StringComparison.Ordinal))
            {
                continue;
            }

            count++;
            if (count > MaximumSiblingEntries)
            {
                throw new InvalidDataException(
                    $"{label} parent exceeds {MaximumSiblingEntries} entries");
            }

            var name = Path.GetFileName(entry);
            if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, expectedName, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{label} path has a portable case alias");
            }
        }

        return count;
    }

    private static void EnsureSiblingCapacity(
        int currentCount,
        int requiredEntries,
        string label)
    {
        if (currentCount + requiredEntries > MaximumSiblingEntries)
        {
            throw new InvalidDataException(
                $"{label} parent exceeds {MaximumSiblingEntries} entries after reserving output");
        }
    }

    private static void RejectLink(string path, string label)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"{label} must not be a link");
        }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static byte[] ReadBounded(string path, string label)
    {
        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.SequentialScan);
        var initialLength = input.Length;
        EnsureBounded(checked((int)Math.Min(initialLength, int.MaxValue)), label);

        using var output = new MemoryStream(
            initialLength <= MaximumBytes ? checked((int)initialLength) : 0);
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = input.Read(buffer);
            if (read == 0)
            {
                break;
            }

            EnsureBounded(checked((int)output.Length + read), label);
            output.Write(buffer, 0, read);
        }

        if (input.Length != initialLength || output.Length != initialLength)
        {
            throw new InvalidDataException($"{label} changed while it was read");
        }

        return output.ToArray();
    }

    private static void WriteAtomic(
        string root,
        string[] parts,
        string path,
        string label,
        ReadOnlySpan<byte> bytes)
    {
        var temporary = path + $".tmp-{Guid.NewGuid():N}";
        Exception? primaryFailure = null;
        try
        {
            using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }

            var revalidated = ResolveWritablePath(root, parts, label, temporary);
            if (!string.Equals(revalidated, path, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{label} path changed before replacement");
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            RunCleanup(primaryFailure, () =>
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            });
        }
    }
}

internal static class CanonicalFixtureJson
{
    internal static byte[] Render(
        string label,
        Action<Utf8JsonWriter> writeFixture)
    {
        ArgumentNullException.ThrowIfNull(writeFixture);
        using var stream = new BoundedMemoryStream(label);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writeFixture(writer);
            writer.Flush();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private sealed class BoundedMemoryStream(string label) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityFor(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityFor(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacityFor(1);
            base.WriteByte(value);
        }

        private void EnsureCapacityFor(int additionalBytes)
        {
            if (additionalBytes > FixedCanonicalFixtureFile.MaximumBytes - Position)
            {
                throw new InvalidDataException(
                    $"{label} exceeds {FixedCanonicalFixtureFile.MaximumBytes} bytes");
            }
        }
    }
}
