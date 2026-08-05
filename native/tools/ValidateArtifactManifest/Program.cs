using VibeSnake.Persistence;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine(
        "Usage: ValidateArtifactManifest <path-to-artifact-manifest.json>");
    return 2;
}

var path = Path.GetFullPath(args[0]);
if (!File.Exists(path))
{
    Console.Error.WriteLine("Artifact manifest file not found: " + path);
    return 2;
}

var result = ReleaseArtifactManifest.LoadFromFile(path, enforceRequiredPayload: true);
if (!result.IsSuccess || result.Manifest is null)
{
    Console.Error.WriteLine(
        "ArtifactManifestValidationFailed code="
        + result.Code
        + " message="
        + result.Message);
    return 1;
}

var manifest = result.Manifest;
var shape = ReleaseArtifactManifest.DeclaredInstallerArchiveShape(manifest.Platform);
Console.WriteLine("ArtifactManifestValidated=true");
Console.WriteLine("ArtifactManifestPlatform=" + manifest.Platform);
Console.WriteLine("ArtifactManifestBuildMode=" + manifest.BuildMode);
Console.WriteLine("ArtifactManifestShape=" + shape);
Console.WriteLine("ArtifactManifestFileCount=" + manifest.FileCount);
Console.WriteLine("ArtifactManifestTotalBytes=" + manifest.TotalBytes);
return 0;
