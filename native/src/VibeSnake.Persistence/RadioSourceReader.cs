namespace VibeSnake.Persistence;

/// <summary>
/// Bounded source-checkout MP3 loading. Optional source failures remain isolated
/// from the game, and this reader does not authorize content for export.
/// </summary>
internal static class RadioSourceReader
{
    internal static byte[]? TryRead(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                return null;
            }

            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReadBounded(source, source.Length);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static byte[] ReadBounded(Stream source, long length)
    {
        if (length <= 0 || length > OptionalPackStore.MaximumReadableAssetBytes)
        {
            throw new InvalidDataException("Radio source size exceeds the supported readable range.");
        }

        var bytes = new byte[(int)length];
        source.ReadExactly(bytes);
        if (source.ReadByte() != -1)
        {
            throw new InvalidDataException("Radio source grew while it was being read.");
        }

        return bytes;
    }
}
