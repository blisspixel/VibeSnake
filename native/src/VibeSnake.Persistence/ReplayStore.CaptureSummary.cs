using System.Security.Cryptography;
using System.Text;

namespace VibeSnake.Persistence;

public sealed partial class ReplayStore
{
    public const string CaptureSummaryFileExtension = ".vibesnake-run-summary.json";
    public const int MaximumCaptureSummaryExports = 256;
    public const long MaximumCaptureSummaryExportBytes = 4L * 1024 * 1024;

    public ReplayCaptureSummaryExportResult ExportCaptureSummary(
        string replayId,
        string exportingAppVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replayId);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportingAppVersion);
        if (!IsReplayId(replayId))
        {
            return new ReplayCaptureSummaryExportResult(
                ReplayCaptureSummaryExportCode.InvalidReplayId,
                "The replay identifier is invalid.");
        }

        var loaded = LoadByReplayId(replayId);
        if (loaded.Code == ReplayLoadCode.NotFound)
        {
            return new ReplayCaptureSummaryExportResult(
                ReplayCaptureSummaryExportCode.NotFound,
                "The selected replay no longer exists.");
        }

        if (!loaded.IsSuccess || loaded.Replay is null)
        {
            return new ReplayCaptureSummaryExportResult(
                ReplayCaptureSummaryExportCode.ReplayUnavailable,
                "The selected replay is not verified, so no capture summary was exported.");
        }

        var summary = ReplayCaptureSummary.Create(loaded.Replay, exportingAppVersion);
        var bytes = StrictUtf8.GetBytes(summary.Serialize());
        var fileName = $"run-summary_{replayId}{CaptureSummaryFileExtension}";
        var destination = Path.Combine(ReplayExportDirectory, fileName);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(ReplayDirectory);
            Directory.CreateDirectory(ReplayExportDirectory);
            using var storeLock = TryAcquireStoreLock();
            if (storeLock is null)
            {
                return new ReplayCaptureSummaryExportResult(
                    ReplayCaptureSummaryExportCode.Busy,
                    "The replay library is busy; retry the capture-summary export.",
                    fileName);
            }

            var exports = Directory
                .EnumerateFiles(
                    ReplayExportDirectory,
                    $"run-summary_*{CaptureSummaryFileExtension}",
                    SearchOption.TopDirectoryOnly)
                .Take(MaximumCaptureSummaryExports + 1)
                .Select(path => new FileInfo(path))
                .ToArray();
            var exportBytes = 0L;
            foreach (var export in exports)
            {
                if (export.Length > MaximumCaptureSummaryExportBytes - exportBytes)
                {
                    return Capacity(fileName);
                }

                exportBytes += export.Length;
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (File.Exists(destination))
            {
                return FileContentEquals(destination, bytes)
                    ? new ReplayCaptureSummaryExportResult(
                        ReplayCaptureSummaryExportCode.AlreadyExists,
                        "The identical privacy-safe run summary is already exported.",
                        fileName,
                        sha256)
                    : new ReplayCaptureSummaryExportResult(
                        ReplayCaptureSummaryExportCode.IoFailure,
                        "The summary destination contains different data; nothing was overwritten.",
                        fileName);
            }

            if (exports.Length >= MaximumCaptureSummaryExports
                || bytes.Length > MaximumCaptureSummaryExportBytes - exportBytes)
            {
                return Capacity(fileName);
            }

            temporaryPath = destination + $".tmp-{Guid.NewGuid():N}";
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destination, overwrite: false);
            temporaryPath = null;
            return new ReplayCaptureSummaryExportResult(
                ReplayCaptureSummaryExportCode.Exported,
                "A privacy-safe run summary was exported atomically.",
                fileName,
                sha256);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new ReplayCaptureSummaryExportResult(
                ReplayCaptureSummaryExportCode.IoFailure,
                "The capture-summary export directory is unavailable; nothing was overwritten.",
                fileName);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private static ReplayCaptureSummaryExportResult Capacity(string fileName) => new(
        ReplayCaptureSummaryExportCode.CapacityReached,
        "The bounded capture-summary library is full; existing exports were preserved.",
        fileName);
}
