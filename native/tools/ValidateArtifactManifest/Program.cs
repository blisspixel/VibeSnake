using VibeSnake.Persistence;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Formats.Tar;
using System.IO.Compression;

var packageQualification = args.Length == 9
    && args[1] == "--signing-policy"
    && args[3] == "--readiness-output"
    && args[5] == "--package-qualification"
    && args[7] == "--product-version";
var includeSigningReadiness = (args.Length == 5 || packageQualification)
    && args[1] == "--signing-policy"
    && args[3] == "--readiness-output";
if ((args.Length != 1 && !includeSigningReadiness && !packageQualification)
    || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine(
        "Usage: ValidateArtifactManifest <artifact-manifest.json> "
        + "[--signing-policy <policy.json> --readiness-output <evidence.json> "
        + "[--package-qualification <output-directory> --product-version <version>]]");
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

ReleaseSigningReadiness? signingReadiness = null;
if (includeSigningReadiness)
{
    var policyPath = Path.GetFullPath(args[2]);
    var readinessPath = Path.GetFullPath(args[4]);
    var policyResult = ReleaseSigningPolicy.LoadFromFile(policyPath);
    if (!policyResult.IsSuccess || policyResult.Policy is null)
    {
        Console.Error.WriteLine(
            "ReleaseSigningPolicyValidationFailed code="
            + policyResult.Code
            + " message="
            + policyResult.Message);
        return 1;
    }

    try
    {
        var manifestHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
        signingReadiness = policyResult.Policy.Evaluate(manifest, manifestHash);
        var readinessDirectory = Path.GetDirectoryName(readinessPath)
            ?? throw new InvalidDataException("Signing readiness output has no parent directory.");
        Directory.CreateDirectory(readinessDirectory);
        var json = JsonSerializer.Serialize(
            signingReadiness,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });
        File.WriteAllText(readinessPath, json + "\n", new UTF8Encoding(false));
        Console.WriteLine("ReleaseSigningReadinessValidated=true");
        Console.WriteLine("ReleaseSigningReadiness=" + readinessPath);
        Console.WriteLine(
            "ReleaseSigningPromotionEligible=" + signingReadiness.PromotionEligible);
        Console.WriteLine("ReleaseSigningPromotionStatus=" + signingReadiness.PromotionStatus);
    }
    catch (Exception exception) when (
        exception is InvalidDataException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine("ReleaseSigningReadinessFailed message=" + exception.Message);
        return 1;
    }
}

if (packageQualification)
{
    try
    {
        var outputDirectory = Path.GetFullPath(args[6]);
        var productVersion = args[8];
        var plan = ReleaseOutputPlan.Create(
            manifest,
            signingReadiness
                ?? throw new InvalidDataException("Signing readiness was not generated."),
            productVersion,
            qualificationOnly: true);
        PackageQualification(path, manifest, plan, outputDirectory);
    }
    catch (Exception exception) when (
        exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
    {
        Console.Error.WriteLine("ReleaseOutputQualificationFailed message=" + exception.Message);
        return 1;
    }
}
return 0;

static void PackageQualification(
    string artifactManifestPath,
    ReleaseArtifactManifest manifest,
    ReleaseOutputPlan plan,
    string outputDirectory)
{
    var artifactRoot = Path.GetDirectoryName(artifactManifestPath)
        ?? throw new InvalidDataException("Artifact manifest has no parent directory.");
    artifactRoot = Path.GetFullPath(artifactRoot)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var resolvedOutput = Path.GetFullPath(outputDirectory)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    var artifactPrefix = artifactRoot + Path.DirectorySeparatorChar;
    var outputPrefix = resolvedOutput + Path.DirectorySeparatorChar;
    if (resolvedOutput.Equals(artifactRoot, comparison)
        || resolvedOutput.StartsWith(artifactPrefix, comparison)
        || artifactRoot.StartsWith(outputPrefix, comparison))
    {
        throw new InvalidDataException(
            "Release package output must not overlap the qualified artifact root.");
    }
    if (Directory.Exists(resolvedOutput) || File.Exists(resolvedOutput))
    {
        throw new InvalidDataException("Release package output must not already exist.");
    }

    var expectedFiles = new Dictionary<string, ReleaseArtifactFileEntry>(StringComparer.Ordinal);
    foreach (var entry in manifest.Files)
    {
        if (!expectedFiles.TryAdd(entry.Path, entry))
        {
            throw new InvalidDataException("Artifact manifest contains duplicate package path.");
        }
        var source = ResolveArtifactFile(artifactRoot, entry.Path, comparison);
        var info = new FileInfo(source);
        if (!info.Exists || info.Length != entry.Bytes)
        {
            throw new InvalidDataException(
                "Qualified artifact file size changed before packaging: " + entry.Path + ".");
        }
        var sha256 = HashFile(source);
        if (sha256 != entry.Sha256)
        {
            throw new InvalidDataException(
                "Qualified artifact file hash changed before packaging: " + entry.Path + ".");
        }
    }

    var actualFiles = Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetRelativePath(artifactRoot, file).Replace('\\', '/'))
        .Where(relative => relative != ReleaseArtifactManifest.FileName)
        .ToHashSet(StringComparer.Ordinal);
    if (!actualFiles.SetEquals(expectedFiles.Keys))
    {
        throw new InvalidDataException(
            "Qualified artifact file set changed before packaging.");
    }

    Directory.CreateDirectory(resolvedOutput);
    var packagePath = Path.Combine(resolvedOutput, plan.DirectDownloadFileName);
    var repeatPackagePath = packagePath + ".repeat";
    try
    {
        WriteQualificationPackage(
            manifest,
            artifactRoot,
            packagePath,
            comparison);
        WriteQualificationPackage(
            manifest,
            artifactRoot,
            repeatPackagePath,
            comparison);
        var packageSha256 = HashFile(packagePath);
        var repeatPackageSha256 = HashFile(repeatPackagePath);
        var packageInfo = new FileInfo(packagePath);
        var repeatPackageInfo = new FileInfo(repeatPackagePath);
        if (packageInfo.Length != repeatPackageInfo.Length
            || packageSha256 != repeatPackageSha256)
        {
            throw new InvalidDataException(
                "Repeated qualification package bytes were not deterministic.");
        }
        File.Delete(repeatPackagePath);

        var finalizedPlan = plan with
        {
            PackageBytes = packageInfo.Length,
            PackageSha256 = packageSha256,
            DeterministicRepeatMatched = true,
        };
        var copiedManifestPath = Path.Combine(
            resolvedOutput,
            ReleaseOutputPlan.ArtifactManifestOutput);
        File.Copy(artifactManifestPath, copiedManifestPath, overwrite: false);
        var planPath = Path.Combine(resolvedOutput, "release_output_plan.json");
        File.WriteAllText(
            planPath,
            JsonSerializer.Serialize(
                finalizedPlan,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }) + "\n",
            new UTF8Encoding(false));

        var checksumLines = new[]
        {
            packageSha256 + " *" + Path.GetFileName(packagePath),
            HashFile(copiedManifestPath) + " *" + Path.GetFileName(copiedManifestPath),
            HashFile(planPath) + " *" + Path.GetFileName(planPath),
        };
        var checksumPath = Path.Combine(resolvedOutput, ReleaseOutputPlan.ChecksumOutput);
        File.WriteAllText(
            checksumPath,
            string.Join('\n', checksumLines) + "\n",
            new UTF8Encoding(false));

        Console.WriteLine("ReleaseOutputPlanValidated=true");
        Console.WriteLine(
            "ReleaseOutputQualificationOnly=" + finalizedPlan.QualificationOnly);
        Console.WriteLine(
            "ReleaseOutputPublicationEligible=" + finalizedPlan.PublicationEligible);
        Console.WriteLine("ReleaseOutputPackage=" + packagePath);
        Console.WriteLine("ReleaseOutputPackageSha256=" + packageSha256);
        Console.WriteLine("ReleaseOutputDirectory=" + resolvedOutput);
        Console.WriteLine("ReleaseOutputPlan=" + planPath);
        Console.WriteLine("ReleaseOutputChecksums=" + checksumPath);
    }
    catch
    {
        if (File.Exists(repeatPackagePath))
        {
            File.Delete(repeatPackagePath);
        }
        if (Directory.Exists(resolvedOutput))
        {
            Directory.Delete(resolvedOutput, recursive: true);
        }
        throw;
    }
}

static void WriteQualificationPackage(
    ReleaseArtifactManifest manifest,
    string artifactRoot,
    string packagePath,
    StringComparison comparison)
{
    switch (manifest.Platform)
    {
        case "windows-x64":
            WriteDeterministicZip(artifactRoot, manifest.Files, packagePath, comparison);
            break;
        case "linux-x64":
            WriteDeterministicTarGzip(artifactRoot, manifest.Files, packagePath, comparison);
            break;
        case "macos-universal":
            var macArchive = manifest.Files.SingleOrDefault(entry => entry.Path == "VibeSnake.zip")
                ?? throw new InvalidDataException(
                    "macOS qualification requires exactly one VibeSnake.zip package input.");
            File.Copy(
                ResolveArtifactFile(artifactRoot, macArchive.Path, comparison),
                packagePath,
                overwrite: false);
            break;
        default:
            throw new InvalidDataException("Release output platform is unsupported.");
    }
}

static void WriteDeterministicZip(
    string artifactRoot,
    IReadOnlyList<ReleaseArtifactFileEntry> entries,
    string outputPath,
    StringComparison comparison)
{
    using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
    var timestamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    foreach (var item in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
    {
        var archiveEntry = archive.CreateEntry(item.Path, CompressionLevel.SmallestSize);
        archiveEntry.LastWriteTime = timestamp;
        using var source = new FileStream(
            ResolveArtifactFile(artifactRoot, item.Path, comparison),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            65_536,
            FileOptions.SequentialScan);
        using var destination = archiveEntry.Open();
        source.CopyTo(destination);
    }
}

static void WriteDeterministicTarGzip(
    string artifactRoot,
    IReadOnlyList<ReleaseArtifactFileEntry> entries,
    string outputPath,
    StringComparison comparison)
{
    using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    using var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: false);
    using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
    foreach (var item in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
    {
        using var source = new FileStream(
            ResolveArtifactFile(artifactRoot, item.Path, comparison),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            65_536,
            FileOptions.SequentialScan);
        var executable = item.Path is "VibeSnake.x86_64" or "VibeSnake.sh";
        var entry = new PaxTarEntry(TarEntryType.RegularFile, item.Path)
        {
            DataStream = source,
            ModificationTime = DateTimeOffset.UnixEpoch,
            Uid = 0,
            Gid = 0,
            UserName = string.Empty,
            GroupName = string.Empty,
            Mode = executable
                ? UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
                : UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead
                    | UnixFileMode.OtherRead,
        };
        writer.WriteEntry(entry);
    }
}

static string ResolveArtifactFile(
    string artifactRoot,
    string relativePath,
    StringComparison comparison)
{
    var prefix = artifactRoot + Path.DirectorySeparatorChar;
    var resolved = Path.GetFullPath(Path.Combine(
        artifactRoot,
        relativePath.Replace('/', Path.DirectorySeparatorChar)));
    if (!resolved.StartsWith(prefix, comparison))
    {
        throw new InvalidDataException("Artifact package path escaped its root.");
    }
    return resolved;
}

static string HashFile(string path)
{
    using var stream = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        65_536,
        FileOptions.SequentialScan);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}
