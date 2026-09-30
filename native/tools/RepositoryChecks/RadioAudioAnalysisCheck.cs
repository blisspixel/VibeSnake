using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

internal static class RadioAudioAnalysisCheck
{
    internal const int MaximumJsonBytes = 8 * 1024 * 1024;
    internal const int MaximumWorkers = 8;
    internal const int ExpectedRadioAssets = 95;
    internal const double MinimumDurationSeconds = 30.0;
    internal const double MaximumDurationSeconds = 15.0 * 60.0;
    internal const int MinimumSampleRateHz = 44_100;
    internal const int MaximumSampleRateHz = 192_000;
    internal const double TargetIntegratedLufs = -18.0;
    internal const double LoudnessToleranceLu = 2.0;
    internal const double MaximumTruePeakDbtp = -1.0;
    internal const double SilenceNoiseDbfs = -60.0;
    internal const double MinimumReportedSilenceSeconds = 1.0;
    internal const double MaximumLeadingSilenceSeconds = 2.0;
    internal const double MaximumTrailingSilenceSeconds = 2.0;
    internal const double MaximumInternalSilenceSeconds = 5.0;

    private const string QualificationKind = "vibesnake-radio-audio-qualification-v1";
    private const string MeasurementBasis = "Full decode with FFmpeg EBU R 128 and true-peak measurement";
    private const string SourceChangedFailure = "source changed during analysis";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
    };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = StrictJsonFile.MaximumDepth,
    };
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Ebur128Pattern = new(
        "Integrated loudness:\\s+I:\\s+(?<integrated>-?(?:inf|\\d+(?:\\.\\d+)?))\\s+LUFS"
        + ".*?Loudness range:\\s+LRA:\\s+(?<range>-?(?:inf|\\d+(?:\\.\\d+)?))\\s+LU"
        + ".*?True peak:\\s+Peak:\\s+(?<peak>-?(?:inf|\\d+(?:\\.\\d+)?))\\s+dBFS",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly (string Field, Regex Pattern, bool Count)[] VolumePatterns =
    [
        ("decodedSampleCount", new Regex("\\bn_samples:\\s+(\\d+)\\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled), true),
        ("meanVolumeDbfs", new Regex("\\bmean_volume:\\s+(-?(?:inf|\\d+(?:\\.\\d+)?))\\s+dB\\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled), false),
        ("samplePeakDbfs", new Regex("\\bmax_volume:\\s+(-?(?:inf|\\d+(?:\\.\\d+)?))\\s+dB\\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled), false),
        ("highestBucketSampleCount", new Regex("\\bhistogram_0db:\\s+(\\d+)\\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled), true),
    ];
    private static readonly Regex SilenceEventPattern = new(
        "silence_(?<event>start|end):\\s+(?<time>\\d+(?:\\.\\d+)?)(?:\\s+\\|\\s+silence_duration:\\s+(?<duration>\\d+(?:\\.\\d+)?))?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] StationAssetFields =
    [
        "pendingAssetIds",
        "approvedAssetIds",
        "rejectedAssetIds",
    ];

    internal delegate RadioToolResult RadioToolRunner(
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutSeconds);

    internal static MediaProbe ParseFfprobe(string output)
    {
        JsonNode? node;
        try
        {
            var bytes = StrictUtf8.GetBytes(output);
            StrictJsonFile.RejectDuplicateProperties(bytes);
            node = JsonNode.Parse(bytes, documentOptions: DocumentOptions);
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidDataException
            or DecoderFallbackException
            or ArgumentException)
        {
            throw new RadioAudioAnalysisException(
                "ffprobe did not emit valid JSON: " + StrictJsonFile.SingleLine(exception.Message));
        }

        if (node is not JsonObject root
            || root["streams"] is not JsonArray streams
            || streams.Count != 1
            || streams[0] is not JsonObject stream)
        {
            throw new RadioAudioAnalysisException("ffprobe must report exactly one audio stream");
        }

        if (root["format"] is not JsonObject format)
        {
            throw new RadioAudioAnalysisException("ffprobe must report container metadata");
        }

        if (stream["codec_name"] is not JsonValue codecValue
            || codecValue.GetValueKind() != JsonValueKind.String
            || string.IsNullOrEmpty(codecValue.GetValue<string>()))
        {
            throw new RadioAudioAnalysisException("ffprobe omitted the audio codec");
        }

        string layout;
        if (!stream.ContainsKey("channel_layout"))
        {
            layout = "unknown";
        }
        else if (stream["channel_layout"] is JsonValue layoutValue
            && layoutValue.GetValueKind() == JsonValueKind.String
            && !string.IsNullOrEmpty(layoutValue.GetValue<string>()))
        {
            layout = layoutValue.GetValue<string>();
        }
        else
        {
            throw new RadioAudioAnalysisException("ffprobe emitted an invalid channel layout");
        }

        if (!TryGetInt64(stream["channels"], out var channels) || channels <= 0 || channels > int.MaxValue)
        {
            throw new RadioAudioAnalysisException("ffprobe emitted an invalid channel count");
        }

        var sampleRate = TruncateFinite(FiniteNumber(stream["sample_rate"], "sample rate"), "sample rate");
        var duration = Round(FiniteNumber(OptionalField(format, stream, "duration"), "duration"), 6);
        var bitRate = TruncateFinite(FiniteNumber(OptionalField(format, stream, "bit_rate"), "bit rate"), "bit rate");
        if (format["format_name"] is not JsonValue formatName
            || formatName.GetValueKind() != JsonValueKind.String
            || string.IsNullOrEmpty(formatName.GetValue<string>()))
        {
            throw new RadioAudioAnalysisException("ffprobe omitted the container format");
        }

        return new MediaProbe(
            codecValue.GetValue<string>(),
            formatName.GetValue<string>(),
            sampleRate,
            (int)channels,
            layout,
            duration,
            bitRate);
    }

    internal static LoudnessMeasurement ParseFfmpeg(string output)
    {
        var matches = Ebur128Pattern.Matches(output);
        if (matches.Count != 1)
        {
            throw new RadioAudioAnalysisException("ffmpeg must emit exactly one EBU R 128 summary");
        }

        var match = matches[0];
        var integrated = ParseMeasurementNumber(match.Groups["integrated"].Value, "integrated loudness");
        var range = ParseMeasurementNumber(match.Groups["range"].Value, "loudness range");
        var peak = ParseMeasurementNumber(match.Groups["peak"].Value, "true peak");
        long decodedSamples = 0;
        double meanVolume = 0;
        double samplePeak = 0;
        long highestBucket = 0;
        foreach (var (field, pattern, count) in VolumePatterns)
        {
            var values = pattern.Matches(output);
            if (values.Count == 0)
            {
                if (field == "highestBucketSampleCount")
                {
                    continue;
                }

                throw new RadioAudioAnalysisException("ffmpeg omitted " + field);
            }

            var raw = values[^1].Groups[1].Value;
            if (count)
            {
                if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    throw new RadioAudioAnalysisException(field + " is not numeric");
                }

                if (field == "decodedSampleCount")
                {
                    decodedSamples = parsed;
                }
                else
                {
                    highestBucket = parsed;
                }
            }
            else if (field == "meanVolumeDbfs")
            {
                meanVolume = ParseMeasurementNumber(raw, field);
            }
            else
            {
                samplePeak = ParseMeasurementNumber(raw, field);
            }
        }

        return new LoudnessMeasurement(
            Round(integrated, 1),
            Round(range, 1),
            Round(peak, 1),
            decodedSamples,
            Round(meanVolume, 1),
            Round(samplePeak, 1),
            highestBucket);
    }

    internal static SilenceMeasurement ParseSilence(string output, double durationSeconds)
    {
        var duration = FiniteDouble(durationSeconds, "track duration");
        double? openStart = null;
        var intervals = new List<(double Start, double End, double Duration)>();
        foreach (Match match in SilenceEventPattern.Matches(output))
        {
            var eventName = match.Groups["event"].Value;
            var eventTime = ParseMeasurementNumber(match.Groups["time"].Value, "silence " + eventName);
            if (eventTime < 0.0 || eventTime > duration + 0.1)
            {
                throw new RadioAudioAnalysisException("silence event is outside the track duration");
            }

            if (eventName == "start")
            {
                if (openStart is not null)
                {
                    throw new RadioAudioAnalysisException("ffmpeg emitted nested silence intervals");
                }

                openStart = eventTime;
                continue;
            }

            if (openStart is null)
            {
                throw new RadioAudioAnalysisException("ffmpeg emitted a silence end without a start");
            }

            var intervalDuration = eventTime - openStart.Value;
            var reported = match.Groups["duration"];
            if (reported.Success
                && Math.Abs(double.Parse(reported.Value, CultureInfo.InvariantCulture) - intervalDuration) > 0.02)
            {
                throw new RadioAudioAnalysisException("ffmpeg silence duration disagrees with its interval");
            }

            intervals.Add((openStart.Value, eventTime, intervalDuration));
            openStart = null;
        }

        if (openStart is not null)
        {
            intervals.Add((openStart.Value, duration, duration - openStart.Value));
        }

        var leading = intervals.Count > 0 && intervals[0].Start <= 0.05 ? intervals[0].Duration : 0.0;
        var trailing = intervals.Count > 0 && intervals[^1].End >= duration - 0.1 ? intervals[^1].Duration : 0.0;
        double? maximumInternal = null;
        for (var index = 0; index < intervals.Count; index++)
        {
            var isLeading = index == 0 && leading > 0.0;
            var isTrailing = index == intervals.Count - 1 && trailing > 0.0;
            if (isLeading || isTrailing)
            {
                continue;
            }

            maximumInternal = maximumInternal is null
                ? intervals[index].Duration
                : Math.Max(maximumInternal.Value, intervals[index].Duration);
        }

        return new SilenceMeasurement(
            intervals.Count,
            Round(intervals.Sum(interval => interval.Duration), 6),
            Round(leading, 6),
            Round(trailing, 6),
            Round(maximumInternal ?? 0.0, 6));
    }

    internal static RadioLibrary LoadRadioAssets(string repositoryRoot, string inventoryPath, string curationPath)
    {
        var inventoryFile = ReadObject(inventoryPath);
        var curationFile = ReadObject(curationPath);
        var inventory = inventoryFile.Node;
        var curation = curationFile.Node;
        if (!IsInteger(inventory["schemaVersion"], 1) || !IsString(inventory["assetRoot"], "assets"))
        {
            throw new RadioAudioAnalysisException("content inventory identity is unsupported");
        }

        if (!IsInteger(curation["schemaVersion"], 1) || !IsString(curation["planId"], "vibesnake-content-curation-v1"))
        {
            throw new RadioAudioAnalysisException("content curation identity is unsupported");
        }

        if (inventory["policySha256"] is not JsonValue inventoryPolicy
            || inventoryPolicy.GetValueKind() != JsonValueKind.String
            || curation["inventoryPolicySha256"] is not JsonValue curationPolicy
            || curationPolicy.GetValueKind() != JsonValueKind.String
            || !string.Equals(
                inventoryPolicy.GetValue<string>(),
                curationPolicy.GetValue<string>(),
                StringComparison.Ordinal))
        {
            throw new RadioAudioAnalysisException("content curation does not match the inventory policy");
        }

        var assignments = StationAssignments(curation);
        if (inventory["assets"] is not JsonArray rawAssets)
        {
            throw new RadioAudioAnalysisException("content inventory assets must be an array");
        }

        var assetRoot = Path.GetFullPath(Path.Combine(Path.GetFullPath(repositoryRoot), "assets"));
        if (!Directory.Exists(assetRoot) || IsReparse(assetRoot))
        {
            throw new RadioAudioAnalysisException("radio asset root must be a regular directory: " + assetRoot);
        }

        var assets = new List<RadioSourceAsset>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in rawAssets)
        {
            if (entry is not JsonObject asset)
            {
                throw new RadioAudioAnalysisException("content inventory contains a non-object asset");
            }

            if (!IsString(asset["role"], "runtime-radio-track"))
            {
                continue;
            }

            if (asset["id"] is not JsonValue idValue
                || idValue.GetValueKind() != JsonValueKind.String
                || asset["path"] is not JsonValue pathValue
                || pathValue.GetValueKind() != JsonValueKind.String
                || !TryGetInt64(asset["bytes"], out var expectedBytes)
                || asset["sha256"] is not JsonValue shaValue
                || shaValue.GetValueKind() != JsonValueKind.String
                || !Sha256Pattern.IsMatch(shaValue.GetValue<string>()))
            {
                throw new RadioAudioAnalysisException("content inventory contains an invalid radio asset");
            }

            var assetId = idValue.GetValue<string>();
            var relativePath = pathValue.GetValue<string>();
            if (!seenIds.Add(assetId) || !seenPaths.Add(relativePath))
            {
                throw new RadioAudioAnalysisException("content inventory contains a repeated radio asset: " + assetId);
            }

            if (!assignments.TryGetValue(assetId, out var stationId))
            {
                throw new RadioAudioAnalysisException("curation does not assign radio asset: " + assetId);
            }

            var sourcePath = ResolveAssetPath(assetRoot, relativePath);
            if (IsReparse(sourcePath) || !File.Exists(sourcePath))
            {
                throw new RadioAudioAnalysisException("radio asset must be a regular file: " + relativePath);
            }

            assets.Add(new RadioSourceAsset(
                assetId,
                stationId,
                relativePath,
                expectedBytes,
                shaValue.GetValue<string>(),
                sourcePath));
        }

        var radioIds = assets.Select(asset => asset.AssetId).ToHashSet(StringComparer.Ordinal);
        if (!radioIds.SetEquals(assignments.Keys))
        {
            throw new RadioAudioAnalysisException("curation and inventory radio sets differ");
        }

        if (assets.Count != ExpectedRadioAssets)
        {
            throw new RadioAudioAnalysisException(
                "expected "
                + ExpectedRadioAssets.ToString(CultureInfo.InvariantCulture)
                + " radio assets, found "
                + assets.Count.ToString(CultureInfo.InvariantCulture));
        }

        assets.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return new RadioLibrary(
            assets,
            inventoryFile.Sha256,
            curationFile.Sha256,
            inventoryPolicy.GetValue<string>(),
            DecisionStatus(curation));
    }

    internal static TrackMeasurement MeasureAsset(
        RadioSourceAsset asset,
        string ffmpeg,
        string ffprobe,
        int timeoutSeconds,
        RadioToolRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var actualBytes = new FileInfo(asset.SourcePath).Length;
        var sourceSha256 = Sha256(asset.SourcePath);
        var failures = new List<string>();
        if (actualBytes != asset.ExpectedBytes)
        {
            failures.Add("source byte count differs from inventory");
        }

        if (!string.Equals(sourceSha256, asset.ExpectedSha256, StringComparison.Ordinal))
        {
            failures.Add("source SHA-256 differs from inventory");
        }

        var probe = Invoke(runner, ffprobe, ProbeArguments(asset.SourcePath), timeoutSeconds);
        if (probe.ExitCode != 0)
        {
            throw new RadioAudioAnalysisException(
                "ffprobe failed for " + asset.RelativePath + ": " + Tail(probe.StandardError));
        }

        var media = ParseFfprobe(probe.StandardOutput);
        var decode = Invoke(runner, ffmpeg, DecodeArguments(asset.SourcePath), timeoutSeconds);
        if (decode.ExitCode != 0)
        {
            throw new RadioAudioAnalysisException(
                "full decode failed for " + asset.RelativePath + ": " + Tail(decode.StandardError));
        }

        var measurement = ParseFfmpeg(decode.StandardError);
        var silence = ParseSilence(decode.StandardError, media.DurationSeconds);
        if (!string.Equals(Sha256(asset.SourcePath), sourceSha256, StringComparison.Ordinal))
        {
            failures.Add(SourceChangedFailure);
        }

        ApplyAdmission(media, measurement, silence, failures);
        var gain = Round(TargetIntegratedLufs - measurement.IntegratedLufs, 1);
        var predictedPeak = Round(measurement.TruePeakDbtp + gain, 1);
        return new TrackMeasurement
        {
            AssetId = asset.AssetId,
            StationId = asset.StationId,
            Path = asset.RelativePath,
            SourceBytes = actualBytes,
            SourceSha256 = sourceSha256,
            Codec = media.Codec,
            Container = media.Container,
            SampleRateHz = media.SampleRateHz,
            Channels = media.Channels,
            ChannelLayout = media.ChannelLayout,
            DurationSeconds = media.DurationSeconds,
            BitRateBps = media.BitRateBps,
            IntegratedLufs = measurement.IntegratedLufs,
            LoudnessRangeLu = measurement.LoudnessRangeLu,
            TruePeakDbtp = measurement.TruePeakDbtp,
            DecodedSampleCount = measurement.DecodedSampleCount,
            MeanVolumeDbfs = measurement.MeanVolumeDbfs,
            SamplePeakDbfs = measurement.SamplePeakDbfs,
            HighestBucketSampleCount = measurement.HighestBucketSampleCount,
            SilenceIntervalCount = silence.SilenceIntervalCount,
            TotalSilenceSeconds = silence.TotalSilenceSeconds,
            LeadingSilenceSeconds = silence.LeadingSilenceSeconds,
            TrailingSilenceSeconds = silence.TrailingSilenceSeconds,
            MaximumInternalSilenceSeconds = silence.MaximumInternalSilenceSeconds,
            RecommendedLoudnessGainDb = gain,
            PredictedTruePeakAfterGainDbtp = predictedPeak,
            LimiterRequiredAtTarget = predictedPeak > MaximumTruePeakDbtp,
            Passed = failures.Count == 0,
            Failures = failures,
        };
    }

    internal static JsonObject SummarizeStation(
        string stationId,
        IReadOnlyList<TrackMeasurement> rows,
        int decoderErrorCount)
    {
        double? minimumLoudness = null;
        double? maximumLoudness = null;
        double? maximumTruePeak = null;
        double loudnessTotal = 0;
        var passed = 0;
        foreach (var row in rows)
        {
            loudnessTotal += row.IntegratedLufs;
            minimumLoudness = minimumLoudness is null
                ? row.IntegratedLufs
                : Math.Min(minimumLoudness.Value, row.IntegratedLufs);
            maximumLoudness = maximumLoudness is null
                ? row.IntegratedLufs
                : Math.Max(maximumLoudness.Value, row.IntegratedLufs);
            maximumTruePeak = maximumTruePeak is null
                ? row.TruePeakDbtp
                : Math.Max(maximumTruePeak.Value, row.TruePeakDbtp);
            if (row.Passed)
            {
                passed++;
            }
        }

        var measured = rows.Count;
        return new JsonObject
        {
            ["stationId"] = stationId,
            ["trackCount"] = measured + decoderErrorCount,
            ["measuredTrackCount"] = measured,
            ["passedTrackCount"] = passed,
            ["failedTrackCount"] = (measured - passed) + decoderErrorCount,
            ["decoderErrorCount"] = decoderErrorCount,
            ["averageIntegratedLufs"] = measured == 0 ? JsonNode.Parse("null") : JsonValue.Create(Round(loudnessTotal / measured, 2)),
            ["minimumIntegratedLufs"] = NumberOrNull(minimumLoudness),
            ["maximumIntegratedLufs"] = NumberOrNull(maximumLoudness),
            ["maximumTruePeakDbtp"] = NumberOrNull(maximumTruePeak),
            ["loudnessFailureCount"] = CountFailure(rows, "integrated loudness is outside the admission band"),
            ["truePeakFailureCount"] = CountFailure(rows, "true peak exceeds the admission ceiling"),
            ["leadingSilenceFailureCount"] = CountFailure(rows, "leading silence exceeds the admission ceiling"),
            ["trailingSilenceFailureCount"] = CountFailure(rows, "trailing silence exceeds the admission ceiling"),
            ["internalSilenceFailureCount"] = CountFailure(rows, "internal silence exceeds the admission ceiling"),
        };
    }

    internal static IReadOnlyList<string> ApplyFinalSourceIntegritySweep(
        IReadOnlyList<RadioSourceAsset> assets,
        IReadOnlyDictionary<string, string> sourceHashesBefore,
        IList<TrackMeasurement> results,
        IList<DecoderErrorRow> errors)
    {
        var expected = assets.Select(asset => asset.RelativePath).ToHashSet(StringComparer.Ordinal);
        if (sourceHashesBefore.Count != expected.Count
            || sourceHashesBefore.Keys.Any(path => !expected.Contains(path)))
        {
            throw new RadioAudioAnalysisException("source-integrity baseline does not match the radio asset set");
        }

        var resultsByPath = new Dictionary<string, TrackMeasurement>(results.Count, StringComparer.Ordinal);
        foreach (var row in results)
        {
            resultsByPath[row.Path] = row;
        }

        var errorsByPath = new Dictionary<string, DecoderErrorRow>(errors.Count, StringComparer.Ordinal);
        foreach (var error in errors)
        {
            errorsByPath[error.Path] = error;
        }

        var modified = new List<string>();
        foreach (var asset in assets)
        {
            if (string.Equals(Sha256(asset.SourcePath), sourceHashesBefore[asset.RelativePath], StringComparison.Ordinal))
            {
                continue;
            }

            modified.Add(asset.RelativePath);
            if (resultsByPath.TryGetValue(asset.RelativePath, out var row))
            {
                if (!row.Failures.Contains(SourceChangedFailure, StringComparer.Ordinal))
                {
                    row.Failures.Add(SourceChangedFailure);
                }

                row.Passed = false;
            }

            if (errorsByPath.TryGetValue(asset.RelativePath, out var error))
            {
                error.SourceChangedDuringAnalysis = true;
            }
        }

        modified.Sort(StringComparer.Ordinal);
        return modified;
    }

    internal static JsonObject Qualify(
        string repositoryRoot,
        string inventoryPath,
        string curationPath,
        string ffmpeg,
        string ffprobe,
        int workers,
        int timeoutSeconds,
        RadioToolRunner runner,
        DateTimeOffset measuredUtc,
        Action<string>? progress)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (workers is < 1 or > MaximumWorkers)
        {
            throw new RadioAudioAnalysisException(
                "workers must be between 1 and " + MaximumWorkers.ToString(CultureInfo.InvariantCulture));
        }

        if (timeoutSeconds is < 10 or > 600)
        {
            throw new RadioAudioAnalysisException("timeout-seconds must be between 10 and 600");
        }

        var library = LoadRadioAssets(repositoryRoot, inventoryPath, curationPath);
        var ffmpegVersion = ToolVersion(ffmpeg, runner);
        var ffprobeVersion = ToolVersion(ffprobe, runner);
        var baselines = new Dictionary<string, string>(library.Assets.Count, StringComparer.Ordinal);
        foreach (var asset in library.Assets)
        {
            baselines[asset.RelativePath] = Sha256(asset.SourcePath);
        }

        var results = new List<TrackMeasurement>(library.Assets.Count);
        var errors = new List<DecoderErrorRow>();
        var completed = new int[1];
        var gate = new object();
        void MeasureOne(RadioSourceAsset asset)
        {
            TrackMeasurement? row = null;
            DecoderErrorRow? error = null;
            try
            {
                row = MeasureAsset(asset, ffmpeg, ffprobe, timeoutSeconds, runner);
            }
            catch (RadioAudioAnalysisException exception)
            {
                error = new DecoderErrorRow(asset.RelativePath, exception.Message);
            }

            var ticket = Interlocked.Increment(ref completed[0]);
            progress?.Invoke(
                "Measured "
                + ticket.ToString(CultureInfo.InvariantCulture)
                + "/"
                + library.Assets.Count.ToString(CultureInfo.InvariantCulture)
                + ": "
                + asset.RelativePath);
            lock (gate)
            {
                if (row is not null)
                {
                    results.Add(row);
                }
                else if (error is not null)
                {
                    errors.Add(error);
                }
            }
        }

        if (workers == 1)
        {
            foreach (var asset in library.Assets)
            {
                MeasureOne(asset);
            }
        }
        else
        {
            Parallel.ForEach(
                library.Assets,
                new ParallelOptions { MaxDegreeOfParallelism = workers },
                MeasureOne);
        }

        results.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        errors.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        var modified = ApplyFinalSourceIntegritySweep(library.Assets, baselines, results, errors);
        return BuildReport(
            library,
            results,
            errors,
            modified,
            ffmpegVersion,
            ffprobeVersion,
            workers,
            timeoutSeconds,
            measuredUtc);
    }

    internal static string RequireOutputPath(
        string repositoryRoot,
        string outputPath,
        bool replace,
        params string[] protectedPaths)
    {
        var full = Path.GetFullPath(outputPath);
        var root = Path.GetFullPath(repositoryRoot);
        if (IsInside(root, full)
            && !IsInside(Path.Combine(root, "TestResults"), full)
            && !IsInside(Path.Combine(root, "archive"), full))
        {
            throw new RadioAudioAnalysisException(
                "output inside the repository must stay under TestResults or archive");
        }

        foreach (var protectedPath in protectedPaths)
        {
            if (PathsEqual(full, Path.GetFullPath(protectedPath)))
            {
                throw new RadioAudioAnalysisException("radio audio output cannot overwrite an input record");
            }
        }

        if (IsReparse(full) || (Directory.Exists(full) && replace))
        {
            throw new RadioAudioAnalysisException("radio audio output must be a regular file path");
        }

        if (File.Exists(full) || Directory.Exists(full))
        {
            if (!replace)
            {
                throw new RadioAudioAnalysisException(
                    "output already exists; pass --replace to overwrite it: " + full);
            }
        }

        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent) && IsReparse(parent))
        {
            throw new RadioAudioAnalysisException("radio audio output must be a regular file path");
        }

        return full;
    }

    internal static void WriteQualification(string outputPath, JsonObject evidence)
    {
        var parent = Path.GetDirectoryName(outputPath)
            ?? throw new RadioAudioAnalysisException("could not write qualification output: output directory is missing");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(
            parent,
            "." + Path.GetFileName(outputPath) + ".staging." + Guid.NewGuid().ToString("N"));
        try
        {
            var bytes = Serialize(evidence);
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(staging, outputPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RadioAudioAnalysisException(
                "could not write qualification output: " + StrictJsonFile.SingleLine(exception.Message));
        }
        finally
        {
            try
            {
                if (File.Exists(staging))
                {
                    File.Delete(staging);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static string? FindTool(string requested)
    {
        if (string.IsNullOrWhiteSpace(requested) || requested.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return null;
        }

        if (Path.IsPathRooted(requested)
            || requested.Contains(Path.DirectorySeparatorChar)
            || requested.Contains('/'))
        {
            return RegularFile(requested) ? Path.GetFullPath(requested) : null;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var names = OperatingSystem.IsWindows()
            ? new[] { requested, requested + ".exe" }
            : new[] { requested };
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (RegularFile(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    internal static RadioToolResult RunProductionTool(
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutSeconds,
        string workingDirectory)
    {
        try
        {
            var result = BoundedProcessRunner.Run(
                executable,
                arguments,
                workingDirectory,
                TimeSpan.FromSeconds(timeoutSeconds));
            return new RadioToolResult(
                result.ExitCode,
                result.StandardOutput,
                result.StandardError,
                result.TimedOut);
        }
        catch (Exception exception) when (exception is Win32Exception
            or FileNotFoundException
            or DirectoryNotFoundException
            or IOException
            or InvalidOperationException
            or PlatformNotSupportedException)
        {
            throw new RadioAudioAnalysisException(
                "tool execution failed: " + executable + ": " + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    internal static string FormatUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var text = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var fraction = utc.Ticks % TimeSpan.TicksPerSecond;
        if (fraction == 0)
        {
            return text + "Z";
        }

        var microseconds = fraction / 10;
        return text + "." + microseconds.ToString("000000", CultureInfo.InvariantCulture) + "Z";
    }

    private static JsonObject BuildReport(
        RadioLibrary library,
        List<TrackMeasurement> results,
        List<DecoderErrorRow> errors,
        IReadOnlyList<string> modified,
        string ffmpegVersion,
        string ffprobeVersion,
        int workers,
        int timeoutSeconds,
        DateTimeOffset measuredUtc)
    {
        var passedCount = results.Count(row => row.Passed);
        var stationByPath = library.Assets.ToDictionary(
            asset => asset.RelativePath,
            asset => asset.StationId,
            StringComparer.Ordinal);
        var stations = new JsonArray();
        foreach (var stationId in library.Assets.Select(asset => asset.StationId).Distinct().OrderBy(id => id, StringComparer.Ordinal))
        {
            var rows = results.Where(row => row.StationId == stationId).ToArray();
            var stationErrors = errors.Count(error => stationByPath[error.Path] == stationId);
            stations.Add(SummarizeStation(stationId, rows, stationErrors));
        }

        var tracks = new JsonArray();
        foreach (var row in results)
        {
            tracks.Add(TrackJson(row));
        }

        var decoderErrors = new JsonArray();
        foreach (var error in errors)
        {
            var item = new JsonObject
            {
                ["path"] = error.Path,
                ["error"] = error.Error,
            };
            if (error.SourceChangedDuringAnalysis)
            {
                item["sourceChangedDuringAnalysis"] = true;
            }

            decoderErrors.Add(item);
        }

        var modifiedPaths = new JsonArray();
        foreach (var path in modified)
        {
            modifiedPaths.Add(path);
        }

        double? minimumLoudness = null;
        double? maximumLoudness = null;
        double? minimumPeak = null;
        double? maximumPeak = null;
        double durationTotal = 0;
        foreach (var row in results)
        {
            durationTotal += row.DurationSeconds;
            minimumLoudness = minimumLoudness is null ? row.IntegratedLufs : Math.Min(minimumLoudness.Value, row.IntegratedLufs);
            maximumLoudness = maximumLoudness is null ? row.IntegratedLufs : Math.Max(maximumLoudness.Value, row.IntegratedLufs);
            minimumPeak = minimumPeak is null ? row.TruePeakDbtp : Math.Min(minimumPeak.Value, row.TruePeakDbtp);
            maximumPeak = maximumPeak is null ? row.TruePeakDbtp : Math.Max(maximumPeak.Value, row.TruePeakDbtp);
        }

        long sourceBytes = 0;
        foreach (var asset in library.Assets)
        {
            sourceBytes += asset.ExpectedBytes;
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = QualificationKind,
            ["measuredUtc"] = FormatUtc(measuredUtc),
            ["passed"] = results.Count == library.Assets.Count && passedCount == library.Assets.Count,
            ["releaseApproved"] = false,
            ["humanListeningRequired"] = true,
            ["sourceBytesModified"] = modified.Count > 0,
            ["measurementBasis"] = MeasurementBasis,
            ["admissionPolicy"] = new JsonObject
            {
                ["targetIntegratedLufs"] = TargetIntegratedLufs,
                ["loudnessToleranceLu"] = LoudnessToleranceLu,
                ["maximumTruePeakDbtp"] = MaximumTruePeakDbtp,
                ["silenceNoiseDbfs"] = SilenceNoiseDbfs,
                ["minimumReportedSilenceSeconds"] = MinimumReportedSilenceSeconds,
                ["maximumLeadingSilenceSeconds"] = MaximumLeadingSilenceSeconds,
                ["maximumTrailingSilenceSeconds"] = MaximumTrailingSilenceSeconds,
                ["maximumInternalSilenceSeconds"] = MaximumInternalSilenceSeconds,
                ["minimumDurationSeconds"] = MinimumDurationSeconds,
                ["maximumDurationSeconds"] = MaximumDurationSeconds,
                ["minimumSampleRateHz"] = MinimumSampleRateHz,
                ["maximumSampleRateHz"] = MaximumSampleRateHz,
                ["allowedChannelCounts"] = new JsonArray { 1, 2 },
            },
            ["toolchain"] = new JsonObject
            {
                ["ffmpeg"] = ffmpegVersion,
                ["ffprobe"] = ffprobeVersion,
                ["operatingSystem"] = OperatingSystemName(),
                ["operatingSystemRelease"] = RuntimeInformation.OSDescription,
                ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
                ["workers"] = workers,
                ["perTrackTimeoutSeconds"] = timeoutSeconds,
            },
            ["inputs"] = new JsonObject
            {
                ["inventorySha256"] = library.InventorySha256,
                ["curationSha256"] = library.CurationSha256,
                ["inventoryPolicySha256"] = library.InventoryPolicySha256,
                ["curationDecisionStatus"] = library.CurationDecisionStatus,
            },
            ["summary"] = new JsonObject
            {
                ["expectedTrackCount"] = library.Assets.Count,
                ["measuredTrackCount"] = results.Count,
                ["passedTrackCount"] = passedCount,
                ["failedTrackCount"] = library.Assets.Count - passedCount,
                ["decoderErrorCount"] = errors.Count,
                ["modifiedSourceCount"] = modified.Count,
                ["modifiedSourcePaths"] = modifiedPaths,
                ["sourceBytes"] = sourceBytes,
                ["totalDurationSeconds"] = Round(durationTotal, 3),
                ["minimumIntegratedLufs"] = NumberOrNull(minimumLoudness),
                ["maximumIntegratedLufs"] = NumberOrNull(maximumLoudness),
                ["minimumTruePeakDbtp"] = NumberOrNull(minimumPeak),
                ["maximumTruePeakDbtp"] = NumberOrNull(maximumPeak),
                ["loudnessFailureCount"] = CountFailure(results, "integrated loudness is outside the admission band"),
                ["truePeakFailureCount"] = CountFailure(results, "true peak exceeds the admission ceiling"),
                ["leadingSilenceFailureCount"] = CountFailure(results, "leading silence exceeds the admission ceiling"),
                ["trailingSilenceFailureCount"] = CountFailure(results, "trailing silence exceeds the admission ceiling"),
                ["internalSilenceFailureCount"] = CountFailure(results, "internal silence exceeds the admission ceiling"),
            },
            ["stations"] = stations,
            ["tracks"] = tracks,
            ["decoderErrors"] = decoderErrors,
        };
    }

    private static JsonObject TrackJson(TrackMeasurement row)
    {
        var failures = new JsonArray();
        foreach (var failure in row.Failures)
        {
            failures.Add(failure);
        }

        return new JsonObject
        {
            ["assetId"] = row.AssetId,
            ["stationId"] = row.StationId,
            ["path"] = row.Path,
            ["sourceBytes"] = row.SourceBytes,
            ["sourceSha256"] = row.SourceSha256,
            ["codec"] = row.Codec,
            ["container"] = row.Container,
            ["sampleRateHz"] = row.SampleRateHz,
            ["channels"] = row.Channels,
            ["channelLayout"] = row.ChannelLayout,
            ["durationSeconds"] = row.DurationSeconds,
            ["bitRateBps"] = row.BitRateBps,
            ["integratedLufs"] = row.IntegratedLufs,
            ["loudnessRangeLu"] = row.LoudnessRangeLu,
            ["truePeakDbtp"] = row.TruePeakDbtp,
            ["decodedSampleCount"] = row.DecodedSampleCount,
            ["meanVolumeDbfs"] = row.MeanVolumeDbfs,
            ["samplePeakDbfs"] = row.SamplePeakDbfs,
            ["highestBucketSampleCount"] = row.HighestBucketSampleCount,
            ["silenceIntervalCount"] = row.SilenceIntervalCount,
            ["totalSilenceSeconds"] = row.TotalSilenceSeconds,
            ["leadingSilenceSeconds"] = row.LeadingSilenceSeconds,
            ["trailingSilenceSeconds"] = row.TrailingSilenceSeconds,
            ["maximumInternalSilenceSeconds"] = row.MaximumInternalSilenceSeconds,
            ["recommendedLoudnessGainDb"] = row.RecommendedLoudnessGainDb,
            ["predictedTruePeakAfterGainDbtp"] = row.PredictedTruePeakAfterGainDbtp,
            ["limiterRequiredAtTarget"] = row.LimiterRequiredAtTarget,
            ["passed"] = row.Passed,
            ["failures"] = failures,
        };
    }

    private static void ApplyAdmission(
        MediaProbe media,
        LoudnessMeasurement measurement,
        SilenceMeasurement silence,
        List<string> failures)
    {
        if (!string.Equals(media.Codec, "mp3", StringComparison.Ordinal))
        {
            failures.Add("codec is not MP3");
        }

        if (media.Channels is not (1 or 2))
        {
            failures.Add("channel count is not mono or stereo");
        }

        if (media.SampleRateHz < MinimumSampleRateHz || media.SampleRateHz > MaximumSampleRateHz)
        {
            failures.Add("sample rate is outside the admitted range");
        }

        if (media.DurationSeconds < MinimumDurationSeconds || media.DurationSeconds > MaximumDurationSeconds)
        {
            failures.Add("duration is outside the admitted range");
        }

        var minimumLoudness = TargetIntegratedLufs - LoudnessToleranceLu;
        var maximumLoudness = TargetIntegratedLufs + LoudnessToleranceLu;
        if (measurement.IntegratedLufs < minimumLoudness || measurement.IntegratedLufs > maximumLoudness)
        {
            failures.Add("integrated loudness is outside the admission band");
        }

        if (measurement.TruePeakDbtp > MaximumTruePeakDbtp)
        {
            failures.Add("true peak exceeds the admission ceiling");
        }

        if (silence.LeadingSilenceSeconds > MaximumLeadingSilenceSeconds)
        {
            failures.Add("leading silence exceeds the admission ceiling");
        }

        if (silence.TrailingSilenceSeconds > MaximumTrailingSilenceSeconds)
        {
            failures.Add("trailing silence exceeds the admission ceiling");
        }

        if (silence.MaximumInternalSilenceSeconds > MaximumInternalSilenceSeconds)
        {
            failures.Add("internal silence exceeds the admission ceiling");
        }
    }

    private static Dictionary<string, string> StationAssignments(JsonObject curation)
    {
        if (curation["stations"] is not JsonArray stations || stations.Count == 0)
        {
            throw new RadioAudioAnalysisException("curation must contain a nonempty stations array");
        }

        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawStation in stations)
        {
            if (rawStation is not JsonObject station
                || station["id"] is not JsonValue idValue
                || idValue.GetValueKind() != JsonValueKind.String)
            {
                throw new RadioAudioAnalysisException("curation contains an invalid station");
            }

            var stationId = idValue.GetValue<string>();
            foreach (var field in StationAssetFields)
            {
                if (station[field] is not JsonArray assetIds)
                {
                    throw new RadioAudioAnalysisException("curation station " + stationId + " has invalid " + field);
                }

                foreach (var item in assetIds)
                {
                    if (item is not JsonValue itemValue || itemValue.GetValueKind() != JsonValueKind.String)
                    {
                        throw new RadioAudioAnalysisException("curation station " + stationId + " has invalid " + field);
                    }

                    var assetId = itemValue.GetValue<string>();
                    if (!assignments.TryAdd(assetId, stationId))
                    {
                        throw new RadioAudioAnalysisException("curation assigns an asset more than once: " + assetId);
                    }
                }
            }
        }

        return assignments;
    }

    private static string ResolveAssetPath(string assetRoot, string relativePath)
    {
        if (relativePath.Contains('\\')
            || relativePath.Contains('\0')
            || relativePath.Any(char.IsControl)
            || Path.IsPathRooted(relativePath))
        {
            throw new RadioAudioAnalysisException("radio asset escapes the asset root: " + relativePath);
        }

        var combined = assetRoot;
        foreach (var segment in relativePath.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new RadioAudioAnalysisException("radio asset escapes the asset root: " + relativePath);
            }

            combined = Path.Combine(combined, segment);
        }

        var full = Path.GetFullPath(combined);
        if (!IsInside(assetRoot, full) || PathsEqual(assetRoot, full))
        {
            throw new RadioAudioAnalysisException("radio asset escapes the asset root: " + relativePath);
        }

        return full;
    }

    private static ParsedJson ReadObject(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            if (!RegularFile(path))
            {
                throw new RadioAudioAnalysisException(
                    "JSON input is unreadable: " + name + ": radio audio JSON input must be a regular file");
            }

            var info = new FileInfo(path);
            if (info.Length > MaximumJsonBytes)
            {
                throw new RadioAudioAnalysisException(
                    "JSON input exceeds "
                    + MaximumJsonBytes.ToString(CultureInfo.InvariantCulture)
                    + " bytes: "
                    + name);
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > MaximumJsonBytes)
            {
                throw new RadioAudioAnalysisException(
                    "JSON input exceeds "
                    + MaximumJsonBytes.ToString(CultureInfo.InvariantCulture)
                    + " bytes: "
                    + name);
            }

            _ = StrictUtf8.GetString(bytes);
            StrictJsonFile.RejectDuplicateProperties(bytes);
            var node = JsonNode.Parse(bytes, documentOptions: DocumentOptions)
                ?? throw new InvalidDataException("JSON root is missing");
            if (node is not JsonObject obj)
            {
                throw new RadioAudioAnalysisException("JSON input must contain an object: " + name);
            }

            return new ParsedJson(obj, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        catch (RadioAudioAnalysisException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or DecoderFallbackException
            or ArgumentException)
        {
            throw new RadioAudioAnalysisException(
                "JSON input is unreadable: " + name + ": " + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    private static string ToolVersion(string executable, RadioToolRunner runner)
    {
        var result = Invoke(runner, executable, ["-version"], 30);
        if (result.ExitCode != 0)
        {
            throw new RadioAudioAnalysisException("tool version check failed: " + executable);
        }

        var text = result.StandardOutput.Length > 0 ? result.StandardOutput : result.StandardError;
        if (text.Length == 0)
        {
            throw new RadioAudioAnalysisException("tool version check returned no output: " + executable);
        }

        var newline = text.IndexOfAny(['\r', '\n']);
        var first = newline < 0 ? text : text[..newline];
        return first.Trim();
    }

    private static RadioToolResult Invoke(
        RadioToolRunner runner,
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutSeconds)
    {
        var result = runner(executable, arguments, timeoutSeconds);
        if (result.TimedOut)
        {
            throw new RadioAudioAnalysisException(
                "tool execution failed: "
                + executable
                + ": timed out after "
                + timeoutSeconds.ToString(CultureInfo.InvariantCulture)
                + " seconds");
        }

        return result;
    }

    private static string[] ProbeArguments(string sourcePath) =>
    [
        "-v",
        "error",
        "-select_streams",
        "a",
        "-show_entries",
        "format=duration,bit_rate,format_name:stream=codec_name,sample_rate,channels,channel_layout,duration,bit_rate",
        "-of",
        "json",
        sourcePath,
    ];

    private static string[] DecodeArguments(string sourcePath) =>
    [
        "-nostdin",
        "-hide_banner",
        "-nostats",
        "-loglevel",
        "info",
        "-i",
        sourcePath,
        "-map",
        "0:a:0",
        "-af",
        "ebur128=peak=true:framelog=verbose,volumedetect,silencedetect=noise="
            + SilenceNoiseDbfs.ToString("0.0", CultureInfo.InvariantCulture)
            + "dB:d="
            + MinimumReportedSilenceSeconds.ToString("0.0", CultureInfo.InvariantCulture),
        "-f",
        "null",
        "-",
    ];

    private static string Sha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RadioAudioAnalysisException(
                "source read failed: " + path + ": " + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    private static string Tail(string value)
    {
        var stripped = value.Trim();
        return stripped.Length <= 500 ? stripped : stripped[^500..];
    }

    private static JsonNode? OptionalField(JsonObject preferred, JsonObject fallback, string name) =>
        preferred.ContainsKey(name) ? preferred[name] : fallback[name];

    private static int CountFailure(IReadOnlyList<TrackMeasurement> rows, string reason) =>
        rows.Count(row => row.Failures.Contains(reason, StringComparer.Ordinal));

    private static JsonNode NumberOrNull(double? value) =>
        value is double number ? JsonValue.Create(number)! : JsonNode.Parse("null")!;

    private static double FiniteNumber(JsonNode? node, string label)
    {
        if (node is JsonValue value)
        {
            if (value.GetValueKind() == JsonValueKind.Number)
            {
                return FiniteDouble(value.GetValue<double>(), label);
            }

            if (value.GetValueKind() == JsonValueKind.String)
            {
                return ParseMeasurementNumber(value.GetValue<string>(), label);
            }
        }

        throw new RadioAudioAnalysisException(label + " is not numeric");
    }

    private static double ParseMeasurementNumber(string value, string label)
    {
        if (value.Equals("inf", StringComparison.OrdinalIgnoreCase)
            || value.Equals("-inf", StringComparison.OrdinalIgnoreCase)
            || value.Equals("+inf", StringComparison.OrdinalIgnoreCase))
        {
            throw new RadioAudioAnalysisException(label + " is not finite");
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new RadioAudioAnalysisException(label + " is not numeric");
        }

        return FiniteDouble(number, label);
    }

    private static double FiniteDouble(double number, string label)
    {
        if (!double.IsFinite(number))
        {
            throw new RadioAudioAnalysisException(label + " is not finite");
        }

        return number;
    }

    private static int TruncateFinite(double number, string label)
    {
        if (number < int.MinValue || number > int.MaxValue)
        {
            throw new RadioAudioAnalysisException(label + " is not numeric");
        }

        return (int)Math.Truncate(number);
    }

    private static bool TryGetInt64(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue json || json.GetValueKind() != JsonValueKind.Number)
        {
            return false;
        }

        var raw = json.ToJsonString();
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && string.Equals(raw, value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static bool IsInteger(JsonNode? node, long expected) =>
        TryGetInt64(node, out var value) && value == expected;

    private static bool IsString(JsonNode? node, string expected) =>
        node is JsonValue value
        && value.GetValueKind() == JsonValueKind.String
        && string.Equals(value.GetValue<string>(), expected, StringComparison.Ordinal);

    private static string DecisionStatus(JsonObject curation)
    {
        if (!curation.TryGetPropertyValue("decisionStatus", out var node) || node is null)
        {
            return "None";
        }

        if (node is JsonValue value)
        {
            return value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                JsonValueKind.Null => "None",
                JsonValueKind.Number => value.ToJsonString(),
                _ => "None",
            };
        }

        return node.ToJsonString();
    }

    private static string OperatingSystemName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "Darwin";
        }

        return "Unknown";
    }

    private static bool RegularFile(string path) =>
        File.Exists(path) && !Directory.Exists(path) && !IsReparse(path);

    private static bool IsReparse(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsInside(string parent, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (string.Equals(root, full, comparison))
        {
            return true;
        }

        return full.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }

    private static double Round(double value, int digits) =>
        Math.Round(value, digits, MidpointRounding.ToEven);

    private static byte[] Serialize(JsonNode node)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(node, RenderOptions);
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            Array.Resize(ref bytes, bytes.Length + 1);
            bytes[^1] = (byte)'\n';
        }

        return bytes;
    }
}

internal readonly record struct RadioToolResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

internal readonly record struct MediaProbe(
    string Codec,
    string Container,
    int SampleRateHz,
    int Channels,
    string ChannelLayout,
    double DurationSeconds,
    int BitRateBps);

internal readonly record struct LoudnessMeasurement(
    double IntegratedLufs,
    double LoudnessRangeLu,
    double TruePeakDbtp,
    long DecodedSampleCount,
    double MeanVolumeDbfs,
    double SamplePeakDbfs,
    long HighestBucketSampleCount);

internal readonly record struct SilenceMeasurement(
    int SilenceIntervalCount,
    double TotalSilenceSeconds,
    double LeadingSilenceSeconds,
    double TrailingSilenceSeconds,
    double MaximumInternalSilenceSeconds);

internal sealed class RadioSourceAsset(
    string assetId,
    string stationId,
    string relativePath,
    long expectedBytes,
    string expectedSha256,
    string sourcePath)
{
    internal string AssetId { get; } = assetId;

    internal string StationId { get; } = stationId;

    internal string RelativePath { get; } = relativePath;

    internal long ExpectedBytes { get; } = expectedBytes;

    internal string ExpectedSha256 { get; } = expectedSha256;

    internal string SourcePath { get; } = sourcePath;
}

internal sealed class RadioLibrary(
    IReadOnlyList<RadioSourceAsset> assets,
    string inventorySha256,
    string curationSha256,
    string inventoryPolicySha256,
    string curationDecisionStatus)
{
    internal IReadOnlyList<RadioSourceAsset> Assets { get; } = assets;

    internal string InventorySha256 { get; } = inventorySha256;

    internal string CurationSha256 { get; } = curationSha256;

    internal string InventoryPolicySha256 { get; } = inventoryPolicySha256;

    internal string CurationDecisionStatus { get; } = curationDecisionStatus;
}

internal sealed class TrackMeasurement
{
    internal string AssetId { get; init; } = string.Empty;

    internal string StationId { get; init; } = string.Empty;

    internal string Path { get; init; } = string.Empty;

    internal long SourceBytes { get; init; }

    internal string SourceSha256 { get; init; } = string.Empty;

    internal string Codec { get; init; } = string.Empty;

    internal string Container { get; init; } = string.Empty;

    internal int SampleRateHz { get; init; }

    internal int Channels { get; init; }

    internal string ChannelLayout { get; init; } = string.Empty;

    internal double DurationSeconds { get; init; }

    internal int BitRateBps { get; init; }

    internal double IntegratedLufs { get; init; }

    internal double LoudnessRangeLu { get; init; }

    internal double TruePeakDbtp { get; init; }

    internal long DecodedSampleCount { get; init; }

    internal double MeanVolumeDbfs { get; init; }

    internal double SamplePeakDbfs { get; init; }

    internal long HighestBucketSampleCount { get; init; }

    internal int SilenceIntervalCount { get; init; }

    internal double TotalSilenceSeconds { get; init; }

    internal double LeadingSilenceSeconds { get; init; }

    internal double TrailingSilenceSeconds { get; init; }

    internal double MaximumInternalSilenceSeconds { get; init; }

    internal double RecommendedLoudnessGainDb { get; init; }

    internal double PredictedTruePeakAfterGainDbtp { get; init; }

    internal bool LimiterRequiredAtTarget { get; init; }

    internal bool Passed { get; set; }

    internal List<string> Failures { get; init; } = [];
}

internal sealed class DecoderErrorRow(string path, string error)
{
    internal string Path { get; } = path;

    internal string Error { get; } = error;

    internal bool SourceChangedDuringAnalysis { get; set; }
}

internal readonly record struct ParsedJson(JsonObject Node, string Sha256);

internal sealed class RadioAudioAnalysisException : InvalidOperationException
{
    internal RadioAudioAnalysisException(string message)
        : base(message)
    {
    }
}
