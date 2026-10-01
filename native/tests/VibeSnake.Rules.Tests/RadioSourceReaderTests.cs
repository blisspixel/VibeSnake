using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class RadioSourceReaderTests
{
    [Fact]
    public void Reads_regular_sources_and_isolates_missing_empty_or_oversized_files()
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-radio-source-");
        try
        {
            var path = Path.Combine(root.FullName, "track.mp3");
            Assert.Null(RadioSourceReader.TryRead(path));
            Assert.Null(RadioSourceReader.TryRead(root.FullName));
            File.WriteAllBytes(path, []);
            Assert.Null(RadioSourceReader.TryRead(path));
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Equal(new byte[] { 1, 2, 3 }, RadioSourceReader.TryRead(path));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                stream.SetLength(OptionalPackStore.MaximumReadableAssetBytes + 1L);
            }

            Assert.Null(RadioSourceReader.TryRead(path));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(33554433L)]
    [InlineData(long.MaxValue)]
    public void Invalid_source_lengths_are_rejected_before_allocation(long length)
    {
        using var stream = new MemoryStream([1]);
        Assert.Throws<InvalidDataException>(() => RadioSourceReader.ReadBounded(stream, length));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Changed_source_lengths_are_rejected_instead_of_returning_partial_audio()
    {
        using var truncated = new MemoryStream([1]);
        Assert.Throws<EndOfStreamException>(() => RadioSourceReader.ReadBounded(truncated, 2));
        using var grown = new MemoryStream([1, 2]);
        Assert.Throws<InvalidDataException>(() => RadioSourceReader.ReadBounded(grown, 1));
    }
}
