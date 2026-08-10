using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Persistence;

public enum PlayerDataCategory : byte
{
    Preferences = 0,
    Progression = 1,
    PersonalBests = 2,
    Replays = 3,
    OptionalContent = 4,
}

public sealed record PlayerDataResetPlan(
    string BackupId,
    IReadOnlyList<PlayerDataCategory> Categories,
    IReadOnlyList<string> RelativeTargets);

public enum PlayerDataResetCode : byte
{
    Success = 0,
    InvalidPlan = 1,
    BackupAlreadyExists = 2,
    UnsafeEntry = 3,
    ChangedDuringBackup = 4,
    IoError = 5,
}

public sealed record PlayerDataResetResult(
    PlayerDataResetCode Code,
    string Message,
    string? BackupLocation = null,
    int RemovedFileCount = 0)
{
    public bool IsSuccess => Code == PlayerDataResetCode.Success;
}

public enum PlayerDataBackupStatus : byte
{
    Valid = 0,
    Corrupt = 1,
    Incomplete = 2,
}

public sealed record PlayerDataBackupInspection(
    string BackupId,
    string RelativeLocation,
    PlayerDataBackupStatus Status,
    string Message,
    IReadOnlyList<PlayerDataCategory> Categories,
    int FileCount,
    long TotalBytes)
{
    public bool CanRestore => Status == PlayerDataBackupStatus.Valid;
}

public enum PlayerDataRestoreCode : byte
{
    Success = 0,
    NotFound = 1,
    Corrupt = 2,
    Conflict = 3,
    UnsafeEntry = 4,
    IoError = 5,
}

public sealed record PlayerDataRestoreResult(
    PlayerDataRestoreCode Code,
    string Message,
    int RestoredFileCount = 0)
{
    public bool IsSuccess => Code == PlayerDataRestoreCode.Success;
}

/// <summary>
/// Creates verified, non-secret player-data backups before resetting one or
/// more fixed categories. Planning and inspection are read-only. Restore never
/// overwrites newer data and never mutates the source backup.
/// </summary>
public sealed class PlayerDataRecoveryService
{
    public const string BackupsDirectoryName = "backups";
    public const string ManifestFileName = "backup.json";
    public const int MaximumBackups = 64;
    public const int MaximumFilesPerBackup = 16_384;
    public const long MaximumFileBytes = 512L * 1024L * 1024L;
    public const long MaximumBackupBytes = 8L * 1024L * 1024L * 1024L;

    private const int CurrentSchemaVersion = 1;
    private const long MaximumManifestBytes = 4L * 1024L * 1024L;
    private const string PayloadDirectoryName = "payload";
    private const string LockFileName = ".player-data-recovery.lock";

    private static readonly IReadOnlyDictionary<PlayerDataCategory, string[]> CategoryTargets =
        new Dictionary<PlayerDataCategory, string[]>
        {
            [PlayerDataCategory.Preferences] =
            [
                PreferencesDocument.FileName,
                "input",
            ],
            [PlayerDataCategory.Progression] =
            [
                AchievementsDocument.FileName,
                OnboardingProgressDocument.FileName,
                ProgressionDocument.FileName,
                SpectatorLeagueDocument.FileName,
            ],
            [PlayerDataCategory.PersonalBests] =
            [
                PersonalBestDocument.FileName,
                ScoreHistoryDocument.FileName,
            ],
            [PlayerDataCategory.Replays] =
            [
                "replays",
                ReplayStore.ReplayExportDirectoryName,
                OfflineChallengeStore.DirectoryName,
            ],
            [PlayerDataCategory.OptionalContent] =
            [
                OptionalPackStore.PacksDirectoryName,
            ],
        };

    public PlayerDataRecoveryService(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The player-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var volumeRoot = Path.GetPathRoot(UserDataRoot)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.Equals(UserDataRoot, volumeRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The player-data root cannot be a filesystem root.",
                nameof(userDataRoot));
        }

        BackupsDirectory = Path.Combine(UserDataRoot, BackupsDirectoryName);
    }

    public string UserDataRoot { get; }

    public string BackupsDirectory { get; }

    public static string CreateBackupId(DateTimeOffset timestamp, Guid nonce) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"reset-{timestamp.UtcDateTime:yyyyMMddTHHmmssfffZ}-{nonce:N}");

    public PlayerDataResetPlan CreateResetPlan(
        IEnumerable<PlayerDataCategory> categories,
        string backupId)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ValidateBackupId(backupId);
        var selected = categories
            .Distinct()
            .OrderBy(category => category)
            .ToArray();
        if (selected.Length == 0 || selected.Any(category => !CategoryTargets.ContainsKey(category)))
        {
            throw new ArgumentException(
                "At least one supported player-data category is required.",
                nameof(categories));
        }

        var targets = selected
            .SelectMany(category => CategoryTargets[category])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return new PlayerDataResetPlan(backupId, selected, targets);
    }

    public PlayerDataResetResult Reset(PlayerDataResetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!PlanMatchesAllowlist(plan))
        {
            return new PlayerDataResetResult(
                PlayerDataResetCode.InvalidPlan,
                "Reset plan does not match the fixed player-data allowlist.");
        }

        var stagingPath = Path.Combine(BackupsDirectory, ".building-" + plan.BackupId);
        var backupPath = Path.Combine(BackupsDirectory, plan.BackupId);
        try
        {
            Directory.CreateDirectory(UserDataRoot);
            using var operationLock = AcquireOperationLock();
            if (Directory.Exists(stagingPath) || Directory.Exists(backupPath))
            {
                return new PlayerDataResetResult(
                    PlayerDataResetCode.BackupAlreadyExists,
                    "The requested backup identifier already exists.");
            }

            var snapshot = CaptureSnapshot(plan.Categories, UserDataRoot);
            Directory.CreateDirectory(Path.Combine(stagingPath, PayloadDirectoryName));
            CopySnapshot(snapshot, UserDataRoot, Path.Combine(stagingPath, PayloadDirectoryName));
            var manifest = new BackupManifest(
                CurrentSchemaVersion,
                plan.BackupId,
                plan.Categories.ToArray(),
                snapshot);
            WriteManifest(stagingPath, manifest);

            var copied = ValidateBackupDirectory(stagingPath, plan.BackupId);
            if (copied.Status != PlayerDataBackupStatus.Valid)
            {
                return new PlayerDataResetResult(
                    PlayerDataResetCode.ChangedDuringBackup,
                    "Backup verification failed before reset: " + copied.Message);
            }

            Directory.Move(stagingPath, backupPath);
            var current = CaptureSnapshot(plan.Categories, UserDataRoot);
            if (!SnapshotMatches(snapshot, current))
            {
                return new PlayerDataResetResult(
                    PlayerDataResetCode.ChangedDuringBackup,
                    "Player data changed during backup; the verified backup was kept and nothing was reset.",
                    RelativeBackupLocation(plan.BackupId));
            }

            RemoveTargets(plan.RelativeTargets);
            return new PlayerDataResetResult(
                PlayerDataResetCode.Success,
                snapshot.Count == 0
                    ? "No matching player data existed; an empty verified backup was retained."
                    : "Selected player data was backed up, verified, and reset.",
                RelativeBackupLocation(plan.BackupId),
                snapshot.Count);
        }
        catch (UnsafePlayerDataException exception)
        {
            return new PlayerDataResetResult(
                PlayerDataResetCode.UnsafeEntry,
                exception.Message,
                Directory.Exists(backupPath) ? RelativeBackupLocation(plan.BackupId) : null);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or CryptographicException)
        {
            return new PlayerDataResetResult(
                PlayerDataResetCode.IoError,
                "Player-data reset could not complete safely: " + exception.Message,
                Directory.Exists(backupPath) ? RelativeBackupLocation(plan.BackupId) : null);
        }
    }

    public IReadOnlyList<PlayerDataBackupInspection> InspectBackups()
    {
        if (!Directory.Exists(BackupsDirectory))
        {
            return Array.Empty<PlayerDataBackupInspection>();
        }

        try
        {
            return Directory.EnumerateDirectories(BackupsDirectory)
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                .Take(MaximumBackups)
                .Select(InspectDirectoryWithoutThrowing)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return
            [
                new PlayerDataBackupInspection(
                    "unavailable",
                    BackupsDirectoryName,
                    PlayerDataBackupStatus.Corrupt,
                    "Backup directory could not be inspected: " + exception.Message,
                    Array.Empty<PlayerDataCategory>(),
                    0,
                    0),
            ];
        }
    }

    public PlayerDataRestoreResult Restore(string backupId)
    {
        try
        {
            ValidateBackupId(backupId);
        }
        catch (ArgumentException exception)
        {
            return new PlayerDataRestoreResult(PlayerDataRestoreCode.NotFound, exception.Message);
        }

        var backupPath = Path.Combine(BackupsDirectory, backupId);
        if (!Directory.Exists(backupPath))
        {
            return new PlayerDataRestoreResult(
                PlayerDataRestoreCode.NotFound,
                "The selected player-data backup does not exist.");
        }

        var stagingPath = Path.Combine(UserDataRoot, ".restoring-" + backupId);
        try
        {
            using var operationLock = AcquireOperationLock();
            var inspection = InspectDirectoryWithoutThrowing(backupPath);
            if (!inspection.CanRestore)
            {
                return new PlayerDataRestoreResult(
                    PlayerDataRestoreCode.Corrupt,
                    "Backup cannot be restored: " + inspection.Message);
            }

            var manifest = ReadManifest(backupPath);
            var targets = manifest.Categories
                .SelectMany(category => CategoryTargets[category])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (targets.Any(TargetExists))
            {
                return new PlayerDataRestoreResult(
                    PlayerDataRestoreCode.Conflict,
                    "Current data conflicts with this backup. Keep current data or reset the same categories first.");
            }

            if (Directory.Exists(stagingPath))
            {
                return new PlayerDataRestoreResult(
                    PlayerDataRestoreCode.Conflict,
                    "A previous restore staging directory exists; current data was not changed.");
            }

            Directory.CreateDirectory(stagingPath);
            CopySnapshot(
                manifest.Files,
                Path.Combine(backupPath, PayloadDirectoryName),
                stagingPath);
            foreach (var target in targets.OrderBy(path => path, StringComparer.Ordinal))
            {
                var stagedTarget = ResolveRelativePath(stagingPath, target);
                if (File.Exists(stagedTarget))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ResolveUserPath(target))!);
                    File.Move(stagedTarget, ResolveUserPath(target));
                }
                else if (Directory.Exists(stagedTarget))
                {
                    Directory.Move(stagedTarget, ResolveUserPath(target));
                }
            }

            Directory.Delete(stagingPath, recursive: true);
            return new PlayerDataRestoreResult(
                PlayerDataRestoreCode.Success,
                "Verified player data was restored. Restart the game to reload every subsystem.",
                manifest.Files.Count);
        }
        catch (UnsafePlayerDataException exception)
        {
            return new PlayerDataRestoreResult(
                PlayerDataRestoreCode.UnsafeEntry,
                exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or CryptographicException)
        {
            return new PlayerDataRestoreResult(
                PlayerDataRestoreCode.IoError,
                "Player-data restore could not complete safely: " + exception.Message);
        }
    }

    private FileStream AcquireOperationLock()
    {
        Directory.CreateDirectory(UserDataRoot);
        return new FileStream(
            Path.Combine(UserDataRoot, LockFileName),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    private bool PlanMatchesAllowlist(PlayerDataResetPlan plan)
    {
        try
        {
            var expected = CreateResetPlan(plan.Categories, plan.BackupId);
            return expected.Categories.SequenceEqual(plan.Categories)
                && expected.RelativeTargets.SequenceEqual(plan.RelativeTargets, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private List<BackupFile> CaptureSnapshot(
        IReadOnlyList<PlayerDataCategory> categories,
        string sourceRoot)
    {
        var files = new List<BackupFile>();
        long totalBytes = 0;
        foreach (var category in categories)
        {
            foreach (var target in CategoryTargets[category])
            {
                var fullTarget = ResolveRelativePath(sourceRoot, target);
                if (File.Exists(fullTarget))
                {
                    AddFile(category, fullTarget, sourceRoot, files, ref totalBytes);
                }
                else if (Directory.Exists(fullTarget))
                {
                    AssertDirectoryTreeSafe(fullTarget);
                    foreach (var path in Directory.EnumerateFiles(
                        fullTarget,
                        "*",
                        SearchOption.AllDirectories))
                    {
                        AddFile(category, path, sourceRoot, files, ref totalBytes);
                    }
                }
            }
        }

        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToList();
    }

    private static void AddFile(
        PlayerDataCategory category,
        string path,
        string sourceRoot,
        List<BackupFile> files,
        ref long totalBytes)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnsafePlayerDataException("Player data contains a link that cannot be reset safely.");
        }

        if (info.Length > MaximumFileBytes
            || files.Count >= MaximumFilesPerBackup
            || totalBytes > MaximumBackupBytes - info.Length)
        {
            throw new UnsafePlayerDataException("Player data exceeds the bounded backup budget.");
        }

        var relative = NormalizeRelativePath(Path.GetRelativePath(sourceRoot, path));
        files.Add(new BackupFile(category, relative, info.Length, HashFile(path)));
        totalBytes += info.Length;
    }

    private static void AssertDirectoryTreeSafe(string root)
    {
        var rootInfo = new DirectoryInfo(root);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnsafePlayerDataException("Player data contains a linked directory.");
        }

        foreach (var directory in rootInfo.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnsafePlayerDataException("Player data contains a linked directory.");
            }
        }
    }

    private static void CopySnapshot(
        IReadOnlyList<BackupFile> files,
        string sourceRoot,
        string destinationRoot)
    {
        foreach (var file in files)
        {
            var source = ResolveRelativePath(sourceRoot, file.RelativePath);
            var destination = ResolveRelativePath(destinationRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
            var copiedInfo = new FileInfo(destination);
            if (copiedInfo.Length != file.Length
                || !string.Equals(HashFile(destination), file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A copied backup payload failed integrity verification.");
            }
        }
    }

    private void RemoveTargets(IReadOnlyList<string> relativeTargets)
    {
        foreach (var relativeTarget in relativeTargets)
        {
            var target = ResolveUserPath(relativeTarget);
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            else if (Directory.Exists(target))
            {
                AssertDirectoryTreeSafe(target);
                Directory.Delete(target, recursive: true);
            }
        }
    }

    private bool TargetExists(string relativeTarget)
    {
        var path = ResolveUserPath(relativeTarget);
        return File.Exists(path) || Directory.Exists(path);
    }

    private PlayerDataBackupInspection InspectDirectoryWithoutThrowing(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith(".building-", StringComparison.Ordinal))
        {
            return new PlayerDataBackupInspection(
                name[10..],
                BackupsDirectoryName + "/" + name,
                PlayerDataBackupStatus.Incomplete,
                "Backup creation was interrupted; keep it for support or remove it manually.",
                Array.Empty<PlayerDataCategory>(),
                0,
                0);
        }

        try
        {
            return ValidateBackupDirectory(path, name);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or InvalidOperationException
            or JsonException
            or CryptographicException
            or UnsafePlayerDataException)
        {
            return new PlayerDataBackupInspection(
                name,
                BackupsDirectoryName + "/" + name,
                PlayerDataBackupStatus.Corrupt,
                "Backup is corrupt and will not be restored: " + exception.Message,
                Array.Empty<PlayerDataCategory>(),
                0,
                0);
        }
    }

    private PlayerDataBackupInspection ValidateBackupDirectory(string path, string expectedId)
    {
        AssertDirectoryTreeSafe(path);
        var rootFiles = Directory.EnumerateFiles(path)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var rootDirectories = Directory.EnumerateDirectories(path)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!rootFiles.SequenceEqual([ManifestFileName], StringComparer.Ordinal)
            || !rootDirectories.SequenceEqual([PayloadDirectoryName], StringComparer.Ordinal))
        {
            throw new InvalidDataException("Backup root contains an unexpected entry.");
        }

        var manifest = ReadManifest(path);
        if (!string.Equals(manifest.BackupId, expectedId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Backup identifier does not match its directory.");
        }

        var payloadRoot = Path.Combine(path, PayloadDirectoryName);
        var payloadPaths = Directory.EnumerateFiles(
                payloadRoot,
                "*",
                SearchOption.AllDirectories)
            .Select(file => NormalizeRelativePath(Path.GetRelativePath(payloadRoot, file)))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        if (!payloadPaths.SequenceEqual(
            manifest.Files.Select(file => file.RelativePath),
            StringComparer.Ordinal))
        {
            throw new InvalidDataException("Backup payload contains an unexpected file.");
        }

        var actual = CaptureSnapshot(manifest.Categories, payloadRoot);
        if (!SnapshotMatches(manifest.Files, actual))
        {
            throw new InvalidDataException("Backup payload files or hashes do not match the manifest.");
        }

        return new PlayerDataBackupInspection(
            manifest.BackupId,
            RelativeBackupLocation(manifest.BackupId),
            PlayerDataBackupStatus.Valid,
            "Backup payload and manifest are valid.",
            manifest.Categories,
            manifest.Files.Count,
            manifest.Files.Sum(file => file.Length));
    }

    private static bool SnapshotMatches(
        IReadOnlyList<BackupFile> expected,
        IReadOnlyList<BackupFile> actual) =>
        expected.Count == actual.Count
        && expected.Zip(actual).All(pair => pair.First == pair.Second);

    private static void WriteManifest(string backupDirectory, BackupManifest manifest)
    {
        var temporaryPath = Path.Combine(backupDirectory, ManifestFileName + ".tmp");
        var finalPath = Path.Combine(backupDirectory, ManifestFileName);
        File.WriteAllText(
            temporaryPath,
            manifest.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, finalPath);
    }

    private static BackupManifest ReadManifest(string backupDirectory)
    {
        var path = Path.Combine(backupDirectory, ManifestFileName);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException("Backup manifest is missing or too large.");
        }

        return BackupManifest.Read(File.ReadAllText(path));
    }

    private string ResolveUserPath(string relativePath) =>
        ResolveRelativePath(UserDataRoot, relativePath);

    private static string ResolveRelativePath(string root, string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(
            Path.Combine(rootFull, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, comparison))
        {
            throw new UnsafePlayerDataException("Player-data path escaped its allowed root.");
        }

        return full;
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path))
        {
            throw new UnsafePlayerDataException("Player-data path must be relative.");
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new UnsafePlayerDataException("Player-data path contains an unsafe segment.");
        }

        return string.Join('/', segments);
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void ValidateBackupId(string backupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupId);
        if (backupId.Length > 96
            || backupId[0] is '.' or '-'
            || backupId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new ArgumentException(
                "Backup identifier contains unsupported characters.",
                nameof(backupId));
        }
    }

    private static string RelativeBackupLocation(string backupId) =>
        BackupsDirectoryName + "/" + backupId;

    private sealed record BackupFile(
        PlayerDataCategory Category,
        string RelativePath,
        long Length,
        string Sha256);

    private sealed record BackupManifest(
        int SchemaVersion,
        string BackupId,
        IReadOnlyList<PlayerDataCategory> Categories,
        IReadOnlyList<BackupFile> Files)
    {
        public string SerializeCanonical()
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
                writer.WriteString("backupId", BackupId);
                writer.WritePropertyName("categories");
                writer.WriteStartArray();
                foreach (var category in Categories)
                {
                    writer.WriteStringValue(CategoryToWire(category));
                }

                writer.WriteEndArray();
                writer.WritePropertyName("files");
                writer.WriteStartArray();
                foreach (var file in Files)
                {
                    writer.WriteStartObject();
                    writer.WriteString("category", CategoryToWire(file.Category));
                    writer.WriteString("path", file.RelativePath);
                    writer.WriteNumber("length", file.Length);
                    writer.WriteString("sha256", file.Sha256);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
        }

        public static BackupManifest Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("Backup manifest is empty.");
            }

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireObject(root, "backup", ["schemaVersion", "backupId", "categories", "files"]);
            if (!root.TryGetProperty("schemaVersion", out var schema)
                || !schema.TryGetInt32(out var schemaVersion)
                || schemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException("Backup schema is unsupported.");
            }

            var backupId = ReadString(root, "backupId");
            try
            {
                ValidateBackupId(backupId);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Backup identifier is invalid.", exception);
            }
            var categoriesElement = root.GetProperty("categories");
            if (categoriesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Backup categories must be an array.");
            }

            var categories = categoriesElement.EnumerateArray()
                .Select(element => ParseCategory(
                    element.ValueKind == JsonValueKind.String ? element.GetString() : null))
                .ToArray();
            if (categories.Length == 0
                || categories.Distinct().Count() != categories.Length
                || !categories.SequenceEqual(categories.OrderBy(category => category)))
            {
                throw new InvalidDataException("Backup categories must be unique and ordered.");
            }

            var filesElement = root.GetProperty("files");
            if (filesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Backup files must be an array.");
            }

            var files = new List<BackupFile>();
            long totalBytes = 0;
            foreach (var element in filesElement.EnumerateArray())
            {
                RequireObject(element, "backup file", ["category", "path", "length", "sha256"]);
                var category = ParseCategory(ReadString(element, "category"));
                var relativePath = NormalizeRelativePath(ReadString(element, "path"));
                if (!categories.Contains(category)
                    || !PathBelongsToCategory(relativePath, category)
                    || element.GetProperty("length").ValueKind != JsonValueKind.Number
                    || !element.GetProperty("length").TryGetInt64(out var length)
                    || length < 0
                    || length > MaximumFileBytes)
                {
                    throw new InvalidDataException("Backup file has an invalid category, path, or length.");
                }

                var sha256 = ReadString(element, "sha256");
                if (sha256.Length != 64
                    || sha256.Any(character =>
                        !char.IsAsciiHexDigit(character) || char.IsUpper(character))
                    || files.Count >= MaximumFilesPerBackup
                    || totalBytes > MaximumBackupBytes - length)
                {
                    throw new InvalidDataException("Backup file exceeds integrity or size limits.");
                }

                files.Add(new BackupFile(category, relativePath, length, sha256));
                totalBytes += length;
            }

            var ordered = files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
            if (!files.SequenceEqual(ordered)
                || files.Select(file => file.RelativePath).Distinct(StringComparer.Ordinal).Count()
                    != files.Count)
            {
                throw new InvalidDataException("Backup file paths must be unique and ordered.");
            }

            return new BackupManifest(schemaVersion, backupId, categories, files);
        }

        private static bool PathBelongsToCategory(string path, PlayerDataCategory category) =>
            CategoryTargets[category].Any(target =>
                string.Equals(path, target, StringComparison.Ordinal)
                || path.StartsWith(target + "/", StringComparison.Ordinal));

        private static void RequireObject(
            JsonElement element,
            string name,
            string[] allowedProperties)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(name + " must be an object.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!allowedProperties.Contains(property.Name) || !seen.Add(property.Name))
                {
                    throw new InvalidDataException(
                        name + " contains an unknown or duplicate field: " + property.Name);
                }
            }

            if (seen.Count != allowedProperties.Length)
            {
                throw new InvalidDataException(name + " is missing a required field.");
            }
        }

        private static string ReadString(JsonElement element, string field)
        {
            if (!element.TryGetProperty(field, out var value)
                || value.ValueKind != JsonValueKind.String
                || value.GetString() is not { } text)
            {
                throw new InvalidDataException(field + " must be a string.");
            }

            return text;
        }
    }

    private static string CategoryToWire(PlayerDataCategory category) => category switch
    {
        PlayerDataCategory.Preferences => "preferences",
        PlayerDataCategory.Progression => "progression",
        PlayerDataCategory.PersonalBests => "personal-bests",
        PlayerDataCategory.Replays => "replays",
        PlayerDataCategory.OptionalContent => "optional-content",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private static PlayerDataCategory ParseCategory(string? wire) => wire switch
    {
        "preferences" => PlayerDataCategory.Preferences,
        "progression" => PlayerDataCategory.Progression,
        "personal-bests" => PlayerDataCategory.PersonalBests,
        "replays" => PlayerDataCategory.Replays,
        "optional-content" => PlayerDataCategory.OptionalContent,
        _ => throw new InvalidDataException("Backup contains an unknown player-data category."),
    };

    private sealed class UnsafePlayerDataException(string message) : Exception(message);
}
