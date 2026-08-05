using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ReleaseArtifactManifestTests
{
    private static string ValidWindowsJson(
        int fileCount = 5,
        long totalBytes = 15,
        bool includeAllRequired = true) =>
        $$"""
        {
          "schemaVersion": 2,
          "product": "Vibe Snake",
          "platform": "windows-x64",
          "buildMode": "Debug",
          "sourceRevision": "abcdef0123456789abcdef0123456789abcdef01",
          "godotVersion": "4.7.1",
          "godotCommit": "a13da4feb",
          "godotArchiveSha512": "{{Hex(128)}}",
          "godotExecutableSha256": "{{Hex(64)}}",
          "dotnetSdk": "10.0.302",
          "smokeStateHash": "0123456789abcdef",
          "fileCount": {{fileCount}},
          "totalBytes": {{totalBytes}},
          "files": [
            { "path": "VibeSnake.exe", "bytes": 3, "sha256": "{{Hex(64, 'a')}}" },
            { "path": "VibeSnake.pck", "bytes": 3, "sha256": "{{Hex(64, 'b')}}" },
            { "path": "data_VibeSnake.Game_windows_x86_64/VibeSnake.Game.dll", "bytes": 3, "sha256": "{{Hex(64, 'c')}}" },
            { "path": "data_VibeSnake.Game_windows_x86_64/VibeSnake.Persistence.dll", "bytes": 3, "sha256": "{{Hex(64, 'd')}}" },
            { "path": "data_VibeSnake.Game_windows_x86_64/VibeSnake.Rules.dll", "bytes": 3, "sha256": "{{Hex(64, 'e')}}" }
            {{(includeAllRequired ? "" : "")}}
          ],
          "containerEntries": []
        }
        """;

    [Fact]
    public void Parses_valid_windows_manifest_and_reports_portable_folder_shape()
    {
        var result = ReleaseArtifactManifest.Parse(ValidWindowsJson());
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Manifest);
        Assert.Equal(2, result.Manifest.SchemaVersion);
        Assert.Equal("windows-x64", result.Manifest.Platform);
        Assert.Equal(5, result.Manifest.FileCount);
        Assert.Equal(15, result.Manifest.TotalBytes);
        Assert.True(result.Manifest.IsSupportedPlatform);
        Assert.Equal(
            "portable-folder",
            ReleaseArtifactManifest.DeclaredInstallerArchiveShape(result.Manifest.Platform));
        Assert.Null(ReleaseArtifactManifest.ValidateRequiredPayload(result.Manifest));
    }

    [Fact]
    public void Rejects_schema_mismatch_and_empty_document()
    {
        var empty = ReleaseArtifactManifest.Parse("   ");
        Assert.Equal(ReleaseArtifactManifestLoadCode.Empty, empty.Code);

        var badSchema = ReleaseArtifactManifest.Parse(
            """
            {
              "schemaVersion": 1,
              "product": "Vibe Snake",
              "platform": "windows-x64",
              "buildMode": "Debug",
              "sourceRevision": "x",
              "godotVersion": "4.7.1",
              "godotCommit": "c",
              "godotArchiveSha512": "aa",
              "godotExecutableSha256": "bb",
              "dotnetSdk": "10.0.302",
              "smokeStateHash": "0123456789abcdef",
              "fileCount": 0,
              "totalBytes": 0,
              "files": [],
              "containerEntries": []
            }
            """);
        Assert.Equal(ReleaseArtifactManifestLoadCode.UnsupportedSchema, badSchema.Code);
    }

    [Fact]
    public void Rejects_byte_sum_mismatch_and_path_traversal()
    {
        var sumMismatch = ReleaseArtifactManifest.Parse(
            ValidWindowsJson(fileCount: 5, totalBytes: 99));
        Assert.Equal(ReleaseArtifactManifestLoadCode.InvalidField, sumMismatch.Code);
        Assert.Contains("totalBytes", sumMismatch.Message, StringComparison.Ordinal);

        var traversal = ReleaseArtifactManifest.Parse(
            """
            {
              "schemaVersion": 2,
              "product": "Vibe Snake",
              "platform": "windows-x64",
              "buildMode": "Release",
              "sourceRevision": "deadbeef",
              "godotVersion": "4.7.1",
              "godotCommit": "a13da4feb",
              "godotArchiveSha512": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "godotExecutableSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
              "dotnetSdk": "10.0.302",
              "smokeStateHash": "0123456789abcdef",
              "fileCount": 1,
              "totalBytes": 1,
              "files": [
                { "path": "../escape.exe", "bytes": 1, "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" }
              ],
              "containerEntries": []
            }
            """);
        Assert.Equal(ReleaseArtifactManifestLoadCode.InvalidField, traversal.Code);
        Assert.Contains("unsafe", traversal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_missing_required_windows_payload()
    {
        var json = """
        {
          "schemaVersion": 2,
          "product": "Vibe Snake",
          "platform": "windows-x64",
          "buildMode": "Debug",
          "sourceRevision": "deadbeef",
          "godotVersion": "4.7.1",
          "godotCommit": "a13da4feb",
          "godotArchiveSha512": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "godotExecutableSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "dotnetSdk": "10.0.302",
          "smokeStateHash": "0123456789abcdef",
          "fileCount": 1,
          "totalBytes": 1,
          "files": [
            { "path": "readme.txt", "bytes": 1, "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" }
          ],
          "containerEntries": []
        }
        """;
        var result = ReleaseArtifactManifest.Parse(json);
        Assert.Equal(ReleaseArtifactManifestLoadCode.MissingRequiredPayload, result.Code);

        var structuralOnly = ReleaseArtifactManifest.Parse(json, enforceRequiredPayload: false);
        Assert.True(structuralOnly.IsSuccess);
    }

    [Fact]
    public void Macos_shape_requires_zip_and_container_payloads()
    {
        var json = $$"""
        {
          "schemaVersion": 2,
          "product": "Vibe Snake",
          "platform": "macos-universal",
          "buildMode": "Release",
          "sourceRevision": "deadbeef",
          "godotVersion": "4.7.1",
          "godotCommit": "a13da4feb",
          "godotArchiveSha512": "{{Hex(128)}}",
          "godotExecutableSha256": "{{Hex(64)}}",
          "dotnetSdk": "10.0.302",
          "smokeStateHash": "0123456789abcdef",
          "fileCount": 1,
          "totalBytes": 10,
          "files": [
            { "path": "VibeSnake.zip", "bytes": 10, "sha256": "{{Hex(64, '1')}}" }
          ],
          "containerEntries": [
            { "path": "Vibe Snake.app/Contents/MacOS/Vibe Snake", "bytes": 1, "compressedBytes": 1, "sha256": "{{Hex(64, '2')}}" },
            { "path": "Vibe Snake.app/Contents/Resources/VibeSnake.pck", "bytes": 1, "compressedBytes": 1, "sha256": "{{Hex(64, '3')}}" },
            { "path": "VibeSnake.Game.dll", "bytes": 1, "compressedBytes": 1, "sha256": "{{Hex(64, '4')}}" },
            { "path": "VibeSnake.Persistence.dll", "bytes": 1, "compressedBytes": 1, "sha256": "{{Hex(64, '5')}}" },
            { "path": "VibeSnake.Rules.dll", "bytes": 1, "compressedBytes": 1, "sha256": "{{Hex(64, '6')}}" }
          ]
        }
        """;
        var result = ReleaseArtifactManifest.Parse(json);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(
            "app-bundle-zip",
            ReleaseArtifactManifest.DeclaredInstallerArchiveShape("macos-universal"));
    }

    [Fact]
    public void Declared_shapes_cover_all_supported_platforms()
    {
        Assert.Equal(
            "portable-folder",
            ReleaseArtifactManifest.DeclaredInstallerArchiveShape("linux-x64"));
        Assert.Equal("unknown", ReleaseArtifactManifest.DeclaredInstallerArchiveShape("wasm"));
    }

    [Fact]
    public void LoadFromFile_round_trips_a_temp_manifest()
    {
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-manifest-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, ValidWindowsJson());
            var result = ReleaseArtifactManifest.LoadFromFile(path);
            Assert.True(result.IsSuccess, result.Message);
            Assert.Equal("windows-x64", result.Manifest!.Platform);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Accepts_powershell_style_whole_number_doubles()
    {
        // Measure-Object Sum + ConvertTo-Json emits totalBytes/bytes as 15.0 style doubles.
        var json = ValidWindowsJson()
            .Replace("\"fileCount\": 5", "\"fileCount\": 5.0")
            .Replace("\"totalBytes\": 15", "\"totalBytes\": 15.0")
            .Replace("\"bytes\": 3", "\"bytes\": 3.0");
        var result = ReleaseArtifactManifest.Parse(json);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(5, result.Manifest!.FileCount);
        Assert.Equal(15, result.Manifest.TotalBytes);
        Assert.All(result.Manifest.Files, entry => Assert.Equal(3, entry.Bytes));
    }

    private static string Hex(int length, char fill = '0') => new(fill, length);
}
