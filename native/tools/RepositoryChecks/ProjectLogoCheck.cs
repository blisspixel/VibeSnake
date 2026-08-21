using System.Buffers.Binary;
using System.Security.Cryptography;

namespace RepositoryChecks;

public static class ProjectLogoCheck
{
    public const string RelativePath = "assets/images/logo.png";
    public const int ExpectedWidth = 1024;
    public const int ExpectedHeight = 1024;
    public const string ExpectedSha256 =
        "2ca74991f5b6e83a6da178ff6a63673884425610844a55b29ba35bc89b4a901c";

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] IhdrType = [0x49, 0x48, 0x44, 0x52];

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var path = Path.Combine(
            root,
            RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            return Failed($"logo is missing: {RelativePath}");
        }

        byte[] header;
        try
        {
            using var stream = File.OpenRead(path);
            header = new byte[24];
            var read = stream.Read(header, 0, header.Length);
            if (read != header.Length
                || !header.AsSpan(0, 8).SequenceEqual(PngSignature)
                || !header.AsSpan(12, 4).SequenceEqual(IhdrType))
            {
                return Failed($"not a supported PNG logo: {RelativePath}");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failed($"logo is missing: {RelativePath}");
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4));
        if (width != ExpectedWidth || height != ExpectedHeight)
        {
            return Failed(
                $"logo dimensions must be {ExpectedWidth}x{ExpectedHeight}, got {width}x{height}");
        }

        string digest;
        try
        {
            digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failed($"logo is missing: {RelativePath}");
        }

        if (digest != ExpectedSha256)
        {
            return Failed(
                "logo bytes do not match the preferred brand mark; "
                + $"restore {RelativePath} from the approved Snakev2 mark");
        }

        return new RepositoryCheckResult(
            "Project logo",
            true,
            "Project logo check passed.",
            []);
    }

    private static RepositoryCheckResult Failed(string message) =>
        new("Project logo", false, string.Empty, [message]);
}
