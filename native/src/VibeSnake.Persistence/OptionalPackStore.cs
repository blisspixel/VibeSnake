using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace VibeSnake.Persistence;

public sealed record OptionalPackInspectionReport(
    IReadOnlyList<InstalledOptionalPack> Installed,
    IReadOnlyDictionary<string, string> Rejected);

public enum OptionalPackInstallCode : byte
{
    Success = 0,
    InvalidRequest = 1,
    InvalidArchive = 2,
    AlreadyInstalled = 3,
    StoreBusy = 4,
    IoError = 5,
    StorageLimit = 6,
}

public sealed record OptionalPackInstallResult(
    OptionalPackInstallCode Code,
    string Message,
    InstalledOptionalPack? Pack = null)
{
    public bool IsSuccess => Code == OptionalPackInstallCode.Success && Pack is not null;
}

public sealed record InstalledRadioCatalogReport(
    RadioCatalog Catalog,
    IReadOnlyDictionary<string, string> Rejected);

public sealed record QuarantinedOptionalPack(
    string Id,
    string Version,
    string DisplayName,
    OptionalPackQuarantineReceipt Receipt);

public sealed record OptionalPackQuarantineInspectionReport(
    IReadOnlyList<QuarantinedOptionalPack> Available,
    IReadOnlyDictionary<string, string> Rejected);

public enum OptionalPackQuarantineCode : byte
{
    Success = 0,
    StaleConsent = 1,
    InvalidInstalledPack = 2,
    AlreadyRemoved = 3,
    RestoreConflict = 4,
    StorageLimit = 5,
    StoreBusy = 6,
    IoError = 7,
}

public sealed record OptionalPackQuarantineReceipt(
    string PackId,
    string PackVersion,
    string QuarantineName);

public sealed record OptionalPackQuarantineResult(
    OptionalPackQuarantineCode Code,
    string Message,
    OptionalPackQuarantineReceipt? Receipt = null)
{
    public bool IsSuccess => Code == OptionalPackQuarantineCode.Success && Receipt is not null;
}

public enum OptionalPackAssetReadCode : byte
{
    Success = 0,
    InvalidRequest = 1,
    PackNotInstalled = 2,
    InvalidPack = 3,
    AssetNotFound = 4,
    AssetTooLarge = 5,
    ChangedDuringRead = 6,
}

public sealed record InstalledOptionalPackAsset(
    string PackId,
    string PackVersion,
    string AssetId,
    string MediaType,
    byte[] Bytes);

public sealed record OptionalPackAssetReadResult(
    OptionalPackAssetReadCode Code,
    string Message,
    InstalledOptionalPackAsset? Asset = null)
{
    public bool IsSuccess => Code == OptionalPackAssetReadCode.Success && Asset is not null;
}

/// <summary>
/// User-data-only optional-pack discovery and recoverable removal. Removal is a
/// same-volume move into a private quarantine, never a recursive delete.
/// </summary>
public sealed class OptionalPackStore
{
    public const string PacksDirectoryName = "packs";
    public const string ManifestFileName = "pack.json";
    public const int MaximumInstalledPacks = 128;
    public const int MaximumQuarantinedPacks = 256;
    public const int MaximumEntriesPerPack = ContentPackManifest.MaximumFiles + 1;
    public const int MaximumReadableAssetBytes = 32 * 1024 * 1024;

    private const string RemovedDirectoryName = ".removed";
    private const string StagingDirectoryName = ".staging";
    private const string LockFileName = ".optional-pack-store.lock";

    private readonly string _packsRoot;
    private readonly string _removedRoot;
    private readonly StringComparison _pathComparison;

    public OptionalPackStore(string absoluteUserDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteUserDataRoot);
        if (!Path.IsPathFullyQualified(absoluteUserDataRoot))
        {
            throw new ArgumentException(
                "Optional pack storage requires an absolute user-data root.",
                nameof(absoluteUserDataRoot));
        }
        var userDataRoot = Path.GetFullPath(absoluteUserDataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _packsRoot = Path.Combine(userDataRoot, PacksDirectoryName);
        _removedRoot = Path.Combine(_packsRoot, RemovedDirectoryName);
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public string PacksRoot => _packsRoot;

    /// <summary>
    /// Installs one reviewed optional-pack archive through a same-volume staging
    /// directory. The archive must contain only canonical pack.json plus the
    /// exact manifest allowlist, and the source archive is never modified.
    /// </summary>
    public OptionalPackInstallResult InstallArchive(
        string absoluteArchivePath,
        ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (string.IsNullOrWhiteSpace(absoluteArchivePath)
            || !Path.IsPathFullyQualified(absoluteArchivePath)
            || !absoluteArchivePath.EndsWith(".vibesnake-pack.zip", StringComparison.OrdinalIgnoreCase))
        {
            return InstallFailure(
                OptionalPackInstallCode.InvalidRequest,
                "Optional pack import requires one absolute .vibesnake-pack.zip file.");
        }

        string archivePath;
        try
        {
            archivePath = Path.GetFullPath(absoluteArchivePath);
            if (!File.Exists(archivePath))
            {
                return InstallFailure(
                    OptionalPackInstallCode.InvalidRequest,
                    "Optional pack archive does not exist.");
            }
            RejectReparsePoint(archivePath, "Optional pack archive");
            var archiveLength = new FileInfo(archivePath).Length;
            if (!ContentPackBudgets.IsWithinRadioStationCompressedBudget(archiveLength))
            {
                return InstallFailure(
                    OptionalPackInstallCode.InvalidArchive,
                    "Optional pack archive exceeds the compressed-size budget.");
            }
            Directory.CreateDirectory(_packsRoot);
            RejectReparsePoint(_packsRoot, "Optional packs root");
        }
        catch (InvalidDataException)
        {
            return InstallFailure(
                OptionalPackInstallCode.InvalidArchive,
                "Optional pack archive could not be opened safely.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return InstallFailure(
                OptionalPackInstallCode.IoError,
                "Optional pack archive could not be opened safely.");
        }

        FileStream operationLock;
        try
        {
            operationLock = AcquireOperationLock();
        }
        catch (IOException)
        {
            return InstallFailure(
                OptionalPackInstallCode.StoreBusy,
                "Optional pack storage is busy. Try again after the current operation finishes.");
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return InstallFailure(
                OptionalPackInstallCode.IoError,
                "Optional pack storage could not be locked safely.");
        }

        using (operationLock)
        {
            return InstallArchiveLocked(archivePath, inventory);
        }
    }

    public OptionalPackInspectionReport InspectInstalled(ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return InspectInstalledCore(inventory);
    }

    /// <summary>
    /// Builds runtime station and track metadata only after complete installed
    /// pack validation. Invalid packs are isolated and never enter the catalog.
    /// </summary>
    public InstalledRadioCatalogReport InspectRadioCatalog(ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!Directory.Exists(_packsRoot))
        {
            return new InstalledRadioCatalogReport(
                RadioCatalog.Empty,
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        RejectReparsePoint(_packsRoot, "Optional packs root");
        var directories = Directory.EnumerateDirectories(_packsRoot)
            .Where(path => !IsManagedDirectory(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (directories.Length > MaximumInstalledPacks)
        {
            throw new InvalidDataException(
                $"Optional pack count exceeds {MaximumInstalledPacks}.");
        }

        var manifests = new List<ContentPackManifest>();
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var directoryName = Path.GetFileName(directory);
            try
            {
                var manifest = ValidatePackDirectory(directory, inventory);
                if (manifest.Id != directoryName)
                {
                    throw new InvalidDataException(
                        "Optional pack folder name does not match manifest id.");
                }

                manifests.Add(manifest);
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                rejected[directoryName] = BoundReason(exception.Message);
            }
        }

        return new InstalledRadioCatalogReport(
            RadioCatalog.FromValidatedManifests(manifests),
            rejected.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    public OptionalPackQuarantineInspectionReport InspectQuarantined(
        ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!Directory.Exists(_removedRoot))
        {
            return new OptionalPackQuarantineInspectionReport(
                Array.Empty<QuarantinedOptionalPack>(),
                new Dictionary<string, string>(StringComparer.Ordinal));
        }
        RejectReparsePoint(_packsRoot, "Optional packs root");
        RejectReparsePoint(_removedRoot, "Optional pack quarantine root");
        var directories = Directory.EnumerateDirectories(_removedRoot)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (directories.Length > MaximumQuarantinedPacks)
        {
            throw new InvalidDataException(
                $"Optional pack quarantine count exceeds {MaximumQuarantinedPacks}.");
        }

        var available = new List<QuarantinedOptionalPack>();
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            try
            {
                var manifest = ValidatePackDirectory(directory, inventory);
                var receipt = new OptionalPackQuarantineReceipt(
                    manifest.Id,
                    manifest.Version,
                    name);
                if (!IsSafeQuarantineName(receipt))
                {
                    throw new InvalidDataException(
                        "Optional pack quarantine name does not match its manifest.");
                }
                available.Add(new QuarantinedOptionalPack(
                    manifest.Id,
                    manifest.Version,
                    manifest.DisplayName,
                    receipt));
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                rejected[name] = BoundReason(exception.Message);
            }
        }
        return new OptionalPackQuarantineInspectionReport(
            available
                .OrderBy(pack => pack.Id, StringComparer.Ordinal)
                .ThenBy(pack => pack.Version, StringComparer.Ordinal)
                .ToArray(),
            rejected.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    public OptionalPackQuarantineResult Quarantine(
        OptionalPackRemovalConsent consent,
        ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(inventory);

        var operation = OpenQuarantineOperation();
        if (operation.Failure is not null)
        {
            return operation.Failure;
        }

        using (operation.OperationLock!)
        {
            try
            {
                return QuarantineLocked(consent, inventory);
            }
            catch (InvalidDataException)
            {
                return Failure(
                    OptionalPackQuarantineCode.InvalidInstalledPack,
                    "Optional pack storage failed validation and was not changed.");
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
            {
                return Failure(
                    OptionalPackQuarantineCode.IoError,
                    "Optional pack could not be moved safely; no removal was confirmed.");
            }
        }
    }

    private OptionalPackQuarantineResult QuarantineLocked(
        OptionalPackRemovalConsent consent,
        ContentInventory inventory)
    {
        var inspection = InspectInstalledCore(inventory);
        if (inspection.Rejected.ContainsKey(consent.PackId))
        {
            return Failure(
                OptionalPackQuarantineCode.InvalidInstalledPack,
                "Optional pack failed validation and was not moved.");
        }
        var confirmation = consent.Confirm(inspection.Installed);
        if (!confirmation.IsSuccess)
        {
            return Failure(
                OptionalPackQuarantineCode.StaleConsent,
                confirmation.Message);
        }

        var source = ResolveInstalledPackDirectory(consent.PackId);
        if (!Directory.Exists(source))
        {
            return Failure(
                OptionalPackQuarantineCode.AlreadyRemoved,
                "Optional pack is no longer installed.");
        }
        RejectReparsePoint(source, "Installed optional pack directory");
        Directory.CreateDirectory(_removedRoot);
        RejectReparsePoint(_removedRoot, "Optional pack quarantine root");
        var quarantineCount = Directory.EnumerateDirectories(_removedRoot)
            .Take(MaximumQuarantinedPacks)
            .Count();
        if (quarantineCount >= MaximumQuarantinedPacks)
        {
            return Failure(
                OptionalPackQuarantineCode.StorageLimit,
                $"Optional pack quarantine already contains {MaximumQuarantinedPacks} packs.");
        }
        var quarantineName = $"{consent.PackId}-{Guid.NewGuid():N}";
        var destination = ResolveQuarantineDirectory(quarantineName);
        Directory.Move(source, destination);
        return new OptionalPackQuarantineResult(
            OptionalPackQuarantineCode.Success,
            $"{consent.DisplayName} moved to recoverable quarantine. Saves and replays were retained.",
            new OptionalPackQuarantineReceipt(
                consent.PackId,
                consent.PackVersion,
                quarantineName));
    }

    public OptionalPackQuarantineResult Restore(
        OptionalPackQuarantineReceipt receipt,
        ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(inventory);
        if (!IsSafeQuarantineName(receipt))
        {
            return Failure(
                OptionalPackQuarantineCode.InvalidInstalledPack,
                "Optional pack quarantine receipt is invalid.");
        }

        var operation = OpenQuarantineOperation();
        if (operation.Failure is not null)
        {
            return operation.Failure;
        }

        using (operation.OperationLock!)
        {
            try
            {
                return RestoreLocked(receipt, inventory);
            }
            catch (InvalidDataException)
            {
                return Failure(
                    OptionalPackQuarantineCode.InvalidInstalledPack,
                    "Quarantined optional pack failed validation and was not restored.");
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
            {
                return Failure(
                    OptionalPackQuarantineCode.IoError,
                    "Quarantined optional pack could not be restored safely.");
            }
        }
    }

    private OptionalPackQuarantineResult RestoreLocked(
        OptionalPackQuarantineReceipt receipt,
        ContentInventory inventory)
    {
        if (!Directory.Exists(_removedRoot))
        {
            return Failure(
                OptionalPackQuarantineCode.AlreadyRemoved,
                "Quarantined optional pack is unavailable.");
        }
        RejectReparsePoint(_removedRoot, "Optional pack quarantine root");

        var source = ResolveQuarantineDirectory(receipt.QuarantineName);
        if (!Directory.Exists(source))
        {
            return Failure(
                OptionalPackQuarantineCode.AlreadyRemoved,
                "Quarantined optional pack is unavailable.");
        }
        var destination = ResolveInstalledPackDirectory(receipt.PackId);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            return Failure(
                OptionalPackQuarantineCode.RestoreConflict,
                "An installed pack already uses this id.");
        }
        var installedCount = Directory.EnumerateDirectories(_packsRoot)
            .Where(path => !IsManagedDirectory(path))
            .Take(MaximumInstalledPacks)
            .Count();
        if (installedCount >= MaximumInstalledPacks)
        {
            return Failure(
                OptionalPackQuarantineCode.StorageLimit,
                $"Optional pack storage already contains {MaximumInstalledPacks} packs.");
        }

        ContentPackManifest manifest;
        try
        {
            manifest = ValidatePackDirectory(source, inventory);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            return Failure(
                OptionalPackQuarantineCode.InvalidInstalledPack,
                "Quarantined optional pack failed validation and was not restored.");
        }
        if (manifest.Id != receipt.PackId || manifest.Version != receipt.PackVersion)
        {
            return Failure(
                OptionalPackQuarantineCode.InvalidInstalledPack,
                "Quarantined pack no longer matches its receipt.");
        }
        Directory.Move(source, destination);
        return new OptionalPackQuarantineResult(
            OptionalPackQuarantineCode.Success,
            $"{manifest.DisplayName} restored from quarantine.",
            receipt);
    }

    /// <summary>
    /// Reads one manifest-addressed asset only after revalidating the complete
    /// installed pack. The caller receives bytes and media metadata, never a
    /// machine path. A second size/hash check protects the returned snapshot.
    /// </summary>
    public OptionalPackAssetReadResult ReadAsset(
        string packId,
        string assetId,
        ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!IsSafeOptionalPackId(packId)
            || string.IsNullOrWhiteSpace(assetId)
            || assetId.Length > 640
            || !assetId.StartsWith("asset:", StringComparison.Ordinal))
        {
            return ReadFailure(
                OptionalPackAssetReadCode.InvalidRequest,
                "Optional pack asset request is invalid.");
        }
        if (!Directory.Exists(_packsRoot))
        {
            return ReadFailure(
                OptionalPackAssetReadCode.PackNotInstalled,
                "Optional pack is not installed.");
        }

        try
        {
            RejectReparsePoint(_packsRoot, "Optional packs root");
            using var operationLock = AcquireOperationLock();
            var packDirectory = ResolveInstalledPackDirectory(packId);
            if (!Directory.Exists(packDirectory))
            {
                return ReadFailure(
                    OptionalPackAssetReadCode.PackNotInstalled,
                    "Optional pack is not installed.");
            }
            var manifest = ValidatePackDirectory(packDirectory, inventory);
            if (manifest.Id != packId)
            {
                return ReadFailure(
                    OptionalPackAssetReadCode.InvalidPack,
                    "Optional pack folder does not match its manifest.");
            }
            var entry = manifest.Files.SingleOrDefault(file => file.Id == assetId);
            if (entry is null)
            {
                return ReadFailure(
                    OptionalPackAssetReadCode.AssetNotFound,
                    "Optional pack does not contain the requested asset.");
            }
            if (entry.Bytes > MaximumReadableAssetBytes)
            {
                return ReadFailure(
                    OptionalPackAssetReadCode.AssetTooLarge,
                    $"Optional pack asset exceeds {MaximumReadableAssetBytes} readable bytes.");
            }

            var path = ResolveManifestFilePath(packDirectory, entry.Path);
            RejectReparsePoint(path, "Optional pack asset");
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65_536,
                FileOptions.SequentialScan);
            if (stream.Length != entry.Bytes)
            {
                return ReadFailure(
                    OptionalPackAssetReadCode.ChangedDuringRead,
                    "Optional pack asset changed before reading.");
            }
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (sha256 != entry.Sha256)
            {
                return ReadFailure(
                    OptionalPackAssetReadCode.ChangedDuringRead,
                    "Optional pack asset changed before reading.");
            }
            return new OptionalPackAssetReadResult(
                OptionalPackAssetReadCode.Success,
                "Optional pack asset validated.",
                new InstalledOptionalPackAsset(
                    manifest.Id,
                    manifest.Version,
                    entry.Id,
                    entry.MediaType,
                    bytes));
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            return ReadFailure(
                OptionalPackAssetReadCode.InvalidPack,
                "Optional pack asset could not be validated.");
        }
    }

    private OptionalPackInspectionReport InspectInstalledCore(ContentInventory inventory)
    {
        if (!Directory.Exists(_packsRoot))
        {
            return new OptionalPackInspectionReport(
                Array.Empty<InstalledOptionalPack>(),
                new Dictionary<string, string>(StringComparer.Ordinal));
        }
        RejectReparsePoint(_packsRoot, "Optional packs root");
        var directories = Directory.EnumerateDirectories(_packsRoot)
            .Where(path => !IsManagedDirectory(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (directories.Length > MaximumInstalledPacks)
        {
            throw new InvalidDataException(
                $"Optional pack count exceeds {MaximumInstalledPacks}.");
        }

        var installed = new List<InstalledOptionalPack>();
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var directoryName = Path.GetFileName(directory);
            try
            {
                var manifest = ValidatePackDirectory(directory, inventory);
                if (manifest.Id != directoryName)
                {
                    throw new InvalidDataException(
                        "Optional pack folder name does not match manifest id.");
                }
                installed.Add(new InstalledOptionalPack(
                    manifest.Id,
                    manifest.Version,
                    manifest.DisplayName));
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                rejected[directoryName] = BoundReason(exception.Message);
            }
        }
        return new OptionalPackInspectionReport(
            installed.OrderBy(pack => pack.Id, StringComparer.Ordinal).ToArray(),
            rejected.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private ContentPackManifest ValidatePackDirectory(
        string directory,
        ContentInventory inventory)
    {
        RejectReparsePoint(directory, "Optional pack directory");
        var manifestPath = ResolveChildPath(directory, ManifestFileName);
        var manifest = ContentPackManifest.CheckCanonicalFile(manifestPath, inventory);
        if (manifest.Kind != ContentPackKind.Radio)
        {
            throw new InvalidDataException("Installed optional pack must use radio kind.");
        }

        var files = EnumerateSafeFiles(directory);
        var expected = manifest.Files
            .Select(file => file.Path)
            .Append(ManifestFileName)
            .ToHashSet(StringComparer.Ordinal);
        var actual = files.Keys.ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(actual))
        {
            throw new InvalidDataException(
                "Installed optional pack files do not match its manifest allowlist.");
        }

        long totalPayloadBytes = 0;
        foreach (var entry in manifest.Files)
        {
            var fullPath = files[entry.Path];
            var info = new FileInfo(fullPath);
            if (info.Length != entry.Bytes)
            {
                throw new InvalidDataException(
                    $"Installed optional pack file size mismatch: {entry.Id}.");
            }
            totalPayloadBytes = checked(totalPayloadBytes + info.Length);
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65_536,
                FileOptions.SequentialScan);
            var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (sha256 != entry.Sha256)
            {
                throw new InvalidDataException(
                    $"Installed optional pack file hash mismatch: {entry.Id}.");
            }
        }
        if (!ContentPackBudgets.IsWithinRadioStationInstalledBudget(totalPayloadBytes))
        {
            throw new InvalidDataException(
                "Installed optional pack exceeds the radio installed-size budget.");
        }
        return manifest;
    }

    private OptionalPackInstallResult InstallArchiveLocked(
        string archivePath,
        ContentInventory inventory)
    {
        var stagingRoot = ResolveChildPath(_packsRoot, StagingDirectoryName);
        string? stagingPack = null;
        try
        {
            using var archiveStream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65_536,
                FileOptions.SequentialScan);
            using var archive = new ZipArchive(
                archiveStream,
                ZipArchiveMode.Read,
                leaveOpen: false,
                entryNameEncoding: Encoding.UTF8);
            if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntriesPerPack)
            {
                throw new InvalidDataException("Optional pack archive has an invalid entry count.");
            }

            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            var caseFolded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name)
                    || entry.FullName.Contains('\\')
                    || entry.FullName.StartsWith("/", StringComparison.Ordinal)
                    || entry.FullName.Split('/').Any(part => part is "" or "." or "..")
                    || !entries.TryAdd(entry.FullName, entry)
                    || !caseFolded.Add(entry.FullName))
                {
                    throw new InvalidDataException(
                        "Optional pack archive contains an unsafe, duplicate, or directory entry.");
                }
                var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixFileType == 0xA000)
                {
                    throw new InvalidDataException("Optional pack archive cannot contain symbolic links.");
                }
            }

            if (!entries.TryGetValue(ManifestFileName, out var manifestEntry)
                || manifestEntry.Length <= 0
                || manifestEntry.Length > ContentPackManifest.MaximumManifestBytes)
            {
                throw new InvalidDataException("Optional pack archive has no bounded pack.json manifest.");
            }
            var manifestBytes = ReadBoundedEntry(
                manifestEntry,
                ContentPackManifest.MaximumManifestBytes);
            var manifestJson = new UTF8Encoding(false, true).GetString(manifestBytes);
            var manifest = ContentPackManifest.Parse(manifestJson, inventory);
            if (manifest.Kind != ContentPackKind.Radio
                || !string.Equals(manifest.RenderCanonical(), manifestJson, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Optional pack archive manifest must be canonical radio content.");
            }

            var expectedNames = manifest.Files
                .Select(file => file.Path)
                .Append(ManifestFileName)
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedNames.SetEquals(entries.Keys))
            {
                throw new InvalidDataException(
                    "Optional pack archive entries do not match the manifest allowlist.");
            }
            long installedBytes = 0;
            foreach (var file in manifest.Files)
            {
                var entry = entries[file.Path];
                if (entry.Length != file.Bytes)
                {
                    throw new InvalidDataException(
                        "Optional pack archive entry size does not match the manifest.");
                }
                installedBytes = checked(installedBytes + entry.Length);
            }
            if (!ContentPackBudgets.IsWithinRadioStationInstalledBudget(installedBytes))
            {
                throw new InvalidDataException(
                    "Optional pack archive exceeds the installed-size budget.");
            }

            var destination = ResolveInstalledPackDirectory(manifest.Id);
            if (Directory.Exists(destination) || File.Exists(destination))
            {
                return InstallFailure(
                    OptionalPackInstallCode.AlreadyInstalled,
                    "An optional pack with this id is already installed.");
            }
            var installedDirectoryCount = Directory.EnumerateDirectories(_packsRoot)
                .Where(path => !IsManagedDirectory(path))
                .Take(MaximumInstalledPacks)
                .Count();
            if (installedDirectoryCount >= MaximumInstalledPacks)
            {
                return InstallFailure(
                    OptionalPackInstallCode.StorageLimit,
                    $"Optional pack storage already contains {MaximumInstalledPacks} packs.");
            }
            Directory.CreateDirectory(stagingRoot);
            RejectReparsePoint(stagingRoot, "Optional pack staging root");
            stagingPack = ResolveChildPath(stagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingPack);
            File.WriteAllBytes(ResolveChildPath(stagingPack, ManifestFileName), manifestBytes);
            foreach (var file in manifest.Files)
            {
                var destinationPath = ResolveManifestFilePath(stagingPack, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                using var source = entries[file.Path].Open();
                using var target = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    65_536,
                    FileOptions.WriteThrough);
                CopyExactlyBounded(source, target, file.Bytes);
                target.Flush(flushToDisk: true);
            }

            var installed = ValidatePackDirectory(stagingPack, inventory);
            if (installed.Id != manifest.Id || installed.Version != manifest.Version)
            {
                throw new InvalidDataException(
                    "Extracted optional pack identity changed during validation.");
            }
            Directory.Move(stagingPack, destination);
            stagingPack = null;
            return new OptionalPackInstallResult(
                OptionalPackInstallCode.Success,
                $"{installed.DisplayName} installed and verified.",
                new InstalledOptionalPack(installed.Id, installed.Version, installed.DisplayName));
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or DecoderFallbackException
                or OverflowException)
        {
            return InstallFailure(
                OptionalPackInstallCode.InvalidArchive,
                "Optional pack archive failed manifest, allowlist, size, or integrity validation.");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return InstallFailure(
                OptionalPackInstallCode.IoError,
                "Optional pack archive could not be installed safely.");
        }
        finally
        {
            if (stagingPack is not null && Directory.Exists(stagingPack))
            {
                try
                {
                    Directory.Delete(stagingPack, recursive: true);
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException)
                {
                    // A failed import remains isolated under the managed staging root.
                }
            }
        }
    }

    private static byte[] ReadBoundedEntry(ZipArchiveEntry entry, int maximumBytes)
    {
        if (entry.Length < 0 || entry.Length > maximumBytes)
        {
            throw new InvalidDataException("Optional pack archive entry exceeds its read limit.");
        }
        using var source = entry.Open();
        using var output = new MemoryStream(checked((int)entry.Length));
        CopyExactlyBounded(source, output, entry.Length);
        if (output.Length > maximumBytes)
        {
            throw new InvalidDataException("Optional pack archive entry length changed while reading.");
        }
        return output.ToArray();
    }

    private static void CopyExactlyBounded(
        Stream source,
        Stream destination,
        long expectedBytes)
    {
        var buffer = new byte[65_536];
        long copied = 0;
        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }
            copied = checked(copied + read);
            if (copied > expectedBytes)
            {
                throw new InvalidDataException(
                    "Optional pack archive entry expanded beyond its declared size.");
            }
            destination.Write(buffer, 0, read);
        }
        if (copied != expectedBytes)
        {
            throw new InvalidDataException(
                "Optional pack archive entry ended before its declared size.");
        }
    }

    private Dictionary<string, string> EnumerateSafeFiles(string packDirectory)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var directories = new Stack<string>();
        directories.Push(packDirectory);
        var entryCount = 0;
        while (directories.TryPop(out var current))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                entryCount++;
                if (entryCount > MaximumEntriesPerPack * 2)
                {
                    throw new InvalidDataException(
                        "Installed optional pack contains too many filesystem entries.");
                }
                RejectReparsePoint(path, "Optional pack entry");
                if (Directory.Exists(path))
                {
                    directories.Push(path);
                    continue;
                }
                if (!File.Exists(path))
                {
                    throw new InvalidDataException("Optional pack contains an unsupported entry.");
                }
                var relative = Path.GetRelativePath(packDirectory, path).Replace('\\', '/');
                if (!files.TryAdd(relative, path))
                {
                    throw new InvalidDataException(
                        $"Optional pack contains duplicate path: {relative}.");
                }
            }
        }
        if (files.Count > MaximumEntriesPerPack)
        {
            throw new InvalidDataException(
                "Installed optional pack contains too many files.");
        }
        return files;
    }

    private FileStream AcquireOperationLock()
    {
        var path = ResolveChildPath(_packsRoot, LockFileName);
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                RejectReparsePoint(path, "Optional pack store lock");
            }
            if (Directory.Exists(path))
            {
                throw new InvalidDataException(
                    "Optional pack store lock must be a regular file.");
            }
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.WriteThrough);
            RejectReparsePoint(path, "Optional pack store lock");
            return stream;
        }
        catch (IOException exception)
        {
            throw new IOException("Optional pack store is busy.", exception);
        }
    }

    private (FileStream? OperationLock, OptionalPackQuarantineResult? Failure)
        OpenQuarantineOperation()
    {
        try
        {
            Directory.CreateDirectory(_packsRoot);
            RejectReparsePoint(_packsRoot, "Optional packs root");
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return (
                null,
                Failure(
                    OptionalPackQuarantineCode.IoError,
                    "Optional pack storage could not be opened safely."));
        }

        try
        {
            return (AcquireOperationLock(), null);
        }
        catch (IOException)
        {
            return (
                null,
                Failure(
                    OptionalPackQuarantineCode.StoreBusy,
                    "Optional pack storage is busy. Try again after the current operation finishes."));
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return (
                null,
                Failure(
                    OptionalPackQuarantineCode.IoError,
                    "Optional pack storage could not be locked safely."));
        }
    }

    private string ResolveInstalledPackDirectory(string packId) =>
        ResolveChildPath(_packsRoot, packId);

    private string ResolveQuarantineDirectory(string name) =>
        ResolveChildPath(_removedRoot, name);

    private string ResolveManifestFilePath(string packDirectory, string relativePath)
    {
        var resolvedDirectory = Path.GetFullPath(packDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = resolvedDirectory + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(
            resolvedDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(prefix, _pathComparison))
        {
            throw new InvalidDataException("Optional pack asset escaped its pack root.");
        }
        return resolved;
    }

    private string ResolveChildPath(string parent, string child)
    {
        if (string.IsNullOrWhiteSpace(child)
            || child.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidDataException("Optional pack path component is invalid.");
        }
        var resolvedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = resolvedParent + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(resolvedParent, child));
        if (!resolved.StartsWith(prefix, _pathComparison))
        {
            throw new InvalidDataException("Optional pack path escaped its storage root.");
        }
        return resolved;
    }

    private static void RejectReparsePoint(string path, string location)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"{location} cannot be a link or reparse point.");
        }
    }

    private static bool IsSafeQuarantineName(OptionalPackQuarantineReceipt receipt)
    {
        if (!IsSafeOptionalPackId(receipt.PackId))
        {
            return false;
        }
        try
        {
            _ = ContentPackManifest.ParseSemanticVersion(
                receipt.PackVersion,
                "Optional pack quarantine version");
        }
        catch (InvalidDataException)
        {
            return false;
        }

        var prefix = receipt.PackId + "-";
        if (string.IsNullOrWhiteSpace(receipt.QuarantineName)
            || receipt.QuarantineName.Length != prefix.Length + 32
            || !receipt.QuarantineName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }
        return receipt.QuarantineName[prefix.Length..].All(character =>
            char.IsAsciiDigit(character)
            || character is >= 'a' and <= 'f');
    }

    private static bool IsSafeOptionalPackId(string? packId) =>
        !string.IsNullOrWhiteSpace(packId)
        && packId.Length <= 128
        && packId.StartsWith(ContentPackBudgets.RadioPackIdPrefix, StringComparison.Ordinal)
        && packId.Length > ContentPackBudgets.RadioPackIdPrefix.Length
        && !packId.Split(['.', '-']).Any(string.IsNullOrEmpty)
        && packId.All(character =>
            char.IsAsciiLetterLower(character)
            || char.IsAsciiDigit(character)
            || character is '.' or '-');

    private bool IsManagedDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, RemovedDirectoryName, _pathComparison)
            || string.Equals(name, StagingDirectoryName, _pathComparison);
    }

    private static OptionalPackInstallResult InstallFailure(
        OptionalPackInstallCode code,
        string message) => new(code, message);

    private static OptionalPackQuarantineResult Failure(
        OptionalPackQuarantineCode code,
        string message) => new(code, message);

    private static OptionalPackAssetReadResult ReadFailure(
        OptionalPackAssetReadCode code,
        string message) => new(code, message);

    private static string BoundReason(string reason)
    {
        const int maximumLength = 512;
        var sanitized = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }
}
