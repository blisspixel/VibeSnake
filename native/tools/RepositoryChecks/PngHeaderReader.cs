using System.Buffers.Binary;

namespace RepositoryChecks;

internal static class PngHeaderReader
{
    private static readonly byte[] Signature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] HeaderType = [0x49, 0x48, 0x44, 0x52];

    public static string? TryRead(string path, out uint width, out uint height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        width = 0;
        height = 0;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                24,
                FileOptions.SequentialScan);
            Span<byte> header = stackalloc byte[24];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length
                || !header[..8].SequenceEqual(Signature)
                || !header.Slice(12, 4).SequenceEqual(HeaderType))
            {
                return "not a supported PNG file";
            }

            width = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));
            height = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4));
            return width == 0 || height == 0
                ? "PNG dimensions must be positive"
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return "PNG file could not be read";
        }
    }
}
