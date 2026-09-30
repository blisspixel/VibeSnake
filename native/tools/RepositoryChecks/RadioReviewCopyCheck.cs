using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

internal static class RadioReviewCopyCheck
{
    internal const int MaximumJsonBytes = 16 * 1024 * 1024;
    internal const int MaximumWorkers = 4;
    internal const int MinimumTimeoutSeconds = 30;
    internal const int MaximumTimeoutSeconds = 1800;
    internal const double EdgeSilenceRetainSeconds = 0.25;
    internal const double EdgeCorrectionMarginSeconds = 0.1;
    internal const int MaximumNormalizationAttempts = 3;
    internal const double NormalizationLraTargetLu = 50.0;
    internal const string OutputCodec = "flac";
    internal const string OutputSampleFormat = "s16";
    internal const string ReviewSetKind = "vibesnake-radio-review-copy-set-v1";
    internal const string QualificationKind = "vibesnake-radio-audio-qualification-v1";

    private const string ManifestName = "review-copy-manifest.json";

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
    private static readonly Regex StationIdPattern = new(
        "^[a-z0-9]+(?:_[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] LoudnormFields =
    [
        "input_i",
        "input_tp",
        "input_lra",
        "input_thresh",
        "output_i",
        "output_tp",
        "output_lra",
        "output_thresh",
        "target_offset",
    ];

    internal static LoudnormSummary ParseLoudnorm(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var bytes = Encoding.UTF8.GetBytes(output);
        var candidates = new List<string>();
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'{')
            {
                continue;
            }

            var reader = new Utf8JsonReader(bytes.AsSpan(index));
            if (!JsonDocument.TryParseValue(ref reader, out var document))
            {
                continue;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object || !HasLoudnormFields(document.RootElement))
                {
                    continue;
                }

                candidates.Add(document.RootElement.GetRawText());
            }
        }

        if (candidates.Count != 1)
        {
            throw new RadioReviewCopyException("ffmpeg must emit exactly one complete loudnorm JSON summary");
        }

        var raw = JsonNode.Parse(candidates[0]) as JsonObject
            ?? throw new RadioReviewCopyException("ffmpeg must emit exactly one complete loudnorm JSON summary");
        var summary = new LoudnormSummary
        {
            InputIntegratedLufs = Round(Finite(raw["input_i"], "inputIntegratedLufs"), 2),
            InputTruePeakDbtp = Round(Finite(raw["input_tp"], "inputTruePeakDbtp"), 2),
            InputLoudnessRangeLu = Round(Finite(raw["input_lra"], "inputLoudnessRangeLu"), 2),
            InputThresholdLufs = Round(Finite(raw["input_thresh"], "inputThresholdLufs"), 2),
            OutputIntegratedLufs = Round(Finite(raw["output_i"], "outputIntegratedLufs"), 2),
            OutputTruePeakDbtp = Round(Finite(raw["output_tp"], "outputTruePeakDbtp"), 2),
            OutputLoudnessRangeLu = Round(Finite(raw["output_lra"], "outputLoudnessRangeLu"), 2),
            OutputThresholdLufs = Round(Finite(raw["output_thresh"], "outputThresholdLufs"), 2),
            TargetOffsetDb = Round(Finite(raw["target_offset"], "targetOffsetDb"), 2),
        };
        if (raw["normalization_type"] is not JsonValue typeNode
            || typeNode.GetValueKind() != JsonValueKind.String)
        {
            throw new RadioReviewCopyException("ffmpeg emitted an invalid loudnorm normalization type");
        }

        var normalizationType = typeNode.GetValue<string>().ToLowerInvariant();
        if (normalizationType is not ("linear" or "dynamic"))
        {
            throw new RadioReviewCopyException("ffmpeg emitted an invalid loudnorm normalization type");
        }

        summary.NormalizationType = normalizationType;
        return summary;
    }

    internal static TrimPlan ComputeTrimPlan(double durationSeconds, double leadingSilenceSeconds, double trailingSilenceSeconds)
    {
        var duration = Finite(durationSeconds, "duration");
        var leading = Finite(leadingSilenceSeconds, "leading silence");
        var trailing = Finite(trailingSilenceSeconds, "trailing silence");
        if (duration <= 0.0 || leading < 0.0 || trailing < 0.0 || leading + trailing >= duration)
        {
            throw new RadioReviewCopyException("analysis contains invalid edge-silence bounds");
        }

        var start = Math.Max(0.0, leading - EdgeSilenceRetainSeconds);
        var end = duration - Math.Max(0.0, trailing - EdgeSilenceRetainSeconds);
        var outputDuration = end - start;
        if (outputDuration < RadioAudioAnalysisCheck.MinimumDurationSeconds)
        {
            throw new RadioReviewCopyException("edge trimming would produce an undersized review copy");
        }

        return new TrimPlan(
            Round(duration, 6),
            Round(start, 6),
            Round(end, 6),
            Round(outputDuration, 6),
            EdgeSilenceRetainSeconds);
    }

    internal static string BuildTrimFilter(TrimPlan trim) =>
        "atrim=start=" + Number(trim.StartSeconds) + ":end=" + Number(trim.EndSeconds) + ",asetpts=PTS-STARTPTS";

    internal static string BuildSecondPassFilter(string trimFilter, LoudnormSummary firstPass, int sampleRateHz)
    {
        var parameters = new (string Key, double Value)[]
        {
            ("I", RadioAudioAnalysisCheck.TargetIntegratedLufs),
            ("TP", RadioAudioAnalysisCheck.MaximumTruePeakDbtp),
            ("LRA", NormalizationLraTargetLu),
            ("measured_I", firstPass.InputIntegratedLufs),
            ("measured_LRA", firstPass.InputLoudnessRangeLu),
            ("measured_TP", firstPass.InputTruePeakDbtp),
            ("measured_thresh", firstPass.InputThresholdLufs),
            ("offset", firstPass.TargetOffsetDb),
        };
        var loudnorm = string.Join(
            ":",
            parameters.Select(parameter => parameter.Key + "=" + Number(Finite(parameter.Value, parameter.Key))));
        return trimFilter
            + ",loudnorm="
            + loudnorm
            + ":linear=true:print_format=json,aresample="
            + sampleRateHz.ToString(CultureInfo.InvariantCulture);
    }

    internal static EdgeCorrection ComputeEdgeCorrection(double leadingSilenceSeconds, double trailingSilenceSeconds)
    {
        var leading = Finite(leadingSilenceSeconds, "measured leading silence");
        var trailing = Finite(trailingSilenceSeconds, "measured trailing silence");
        return new EdgeCorrection(
            Round(Math.Max(0.0, leading - RadioAudioAnalysisCheck.MaximumLeadingSilenceSeconds + EdgeCorrectionMarginSeconds), 6),
            Round(Math.Max(0.0, trailing - RadioAudioAnalysisCheck.MaximumTrailingSilenceSeconds + EdgeCorrectionMarginSeconds), 6));
    }

    internal static List<string> ValidateReviewMeasurement(
        MediaProbe media,
        LoudnessMeasurement measurement,
        SilenceMeasurement silence,
        int sourceChannels,
        int sourceSampleRateHz,
        double expectedDurationSeconds)
    {
        var failures = new List<string>();
        if (!string.Equals(media.Codec, OutputCodec, StringComparison.Ordinal))
        {
            failures.Add("review copy codec is not FLAC");
        }

        if (media.Channels != sourceChannels)
        {
            failures.Add("review copy channel count differs from source");
        }

        if (media.SampleRateHz != sourceSampleRateHz)
        {
            failures.Add("review copy sample rate differs from source");
        }

        if (Math.Abs(media.DurationSeconds - expectedDurationSeconds) > 0.1)
        {
            failures.Add("review copy duration differs from the trim plan");
        }

        var minimumLoudness = RadioAudioAnalysisCheck.TargetIntegratedLufs - RadioAudioAnalysisCheck.LoudnessToleranceLu;
        var maximumLoudness = RadioAudioAnalysisCheck.TargetIntegratedLufs + RadioAudioAnalysisCheck.LoudnessToleranceLu;
        if (measurement.IntegratedLufs < minimumLoudness || measurement.IntegratedLufs > maximumLoudness)
        {
            failures.Add("review copy integrated loudness is outside the admission band");
        }

        if (measurement.TruePeakDbtp > RadioAudioAnalysisCheck.MaximumTruePeakDbtp)
        {
            failures.Add("review copy true peak exceeds the admission ceiling");
        }

        if (silence.LeadingSilenceSeconds > RadioAudioAnalysisCheck.MaximumLeadingSilenceSeconds)
        {
            failures.Add("review copy leading silence exceeds the admission ceiling");
        }

        if (silence.TrailingSilenceSeconds > RadioAudioAnalysisCheck.MaximumTrailingSilenceSeconds)
        {
            failures.Add("review copy trailing silence exceeds the admission ceiling");
        }

        if (silence.MaximumInternalSilenceSeconds > RadioAudioAnalysisCheck.MaximumInternalSilenceSeconds)
        {
            failures.Add("review copy internal silence exceeds the admission ceiling");
        }

        return failures;
    }

    internal static void ValidateReplaceTarget(string path, string stationId)
    {
        if (IsReparse(path) || !Directory.Exists(path))
        {
            throw new RadioReviewCopyException("refusing to replace a non-directory review set: " + path);
        }

        var manifestPath = Path.Combine(path, ManifestName);
        if (IsReparse(manifestPath) || !File.Exists(manifestPath))
        {
            throw new RadioReviewCopyException("refusing to replace a review set without its manifest: " + path);
        }

        var manifest = ReadObject(manifestPath).Node;
        if (!IsString(manifest["kind"], ReviewSetKind)
            || !IsInteger(manifest["schemaVersion"], 1)
            || !IsString(manifest["stationId"], stationId))
        {
            throw new RadioReviewCopyException("refusing to replace a review set with a foreign manifest: " + path);
        }
    }

    internal static string RequireOutputRoot(string repositoryRoot, string outputRoot)
    {
        string full;
        try
        {
            full = Path.GetFullPath(outputRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RadioReviewCopyException("radio review output must be a regular directory path");
        }

        if (File.Exists(full) || IsReparse(full))
        {
            throw new RadioReviewCopyException("radio review output must be a regular directory path");
        }

        var root = Path.GetFullPath(repositoryRoot);
        if (IsInside(root, full)
            && !IsInside(Path.Combine(root, "TestResults"), full)
            && !IsInside(Path.Combine(root, "archive"), full))
        {
            throw new RadioReviewCopyException("output inside the repository must stay under TestResults or archive");
        }

        try
        {
            Directory.CreateDirectory(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RadioReviewCopyException(
                "radio review output must be a regular directory path: " + StrictJsonFile.SingleLine(exception.Message));
        }

        if (!Directory.Exists(full) || IsReparse(full))
        {
            throw new RadioReviewCopyException("radio review output must be a regular directory path");
        }

        return full;
    }

    internal static PreparedStation PrepareStation(
        string repositoryRoot,
        string inventoryPath,
        string curationPath,
        string analysisPath,
        string outputRoot,
        string stationId,
        string ffmpeg,
        string ffprobe,
        int workers,
        int timeoutSeconds,
        bool replace,
        RadioAudioAnalysisCheck.RadioToolRunner runner,
        DateTimeOffset createdUtc,
        Action<string>? progress)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (stationId is null || !StationIdPattern.IsMatch(stationId))
        {
            throw new RadioReviewCopyException("station must be a lowercase underscore identifier");
        }

        if (workers is < 1 or > MaximumWorkers)
        {
            throw new RadioReviewCopyException(
                "workers must be between 1 and " + MaximumWorkers.ToString(CultureInfo.InvariantCulture));
        }

        if (timeoutSeconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds)
        {
            throw new RadioReviewCopyException("timeout-seconds must be between 30 and 1800");
        }

        var library = RadioAudioAnalysisCheck.LoadRadioAssets(repositoryRoot, inventoryPath, curationPath);
        var selected = library.Assets.Where(asset => asset.StationId == stationId).ToArray();
        if (selected.Length is < 11 or > 13)
        {
            throw new RadioReviewCopyException("station must contain 11 through 13 tracks: " + stationId);
        }

        var analysisFile = ReadObject(analysisPath);
        var analysis = analysisFile.Node;
        if (!IsString(analysis["kind"], QualificationKind))
        {
            throw new RadioReviewCopyException("radio analysis evidence identity is unsupported");
        }

        if (analysis["sourceBytesModified"] is not JsonValue modifiedFlag
            || modifiedFlag.GetValueKind() != JsonValueKind.False)
        {
            throw new RadioReviewCopyException("radio analysis evidence reports modified source bytes");
        }

        if (!InputsMatch(analysis["inputs"], library))
        {
            throw new RadioReviewCopyException("radio analysis evidence does not match current inventory and curation");
        }

        if (analysis["tracks"] is not JsonArray tracks
            || tracks.Count != library.Assets.Count
            || analysis["decoderErrors"] is not JsonArray decoderErrors
            || decoderErrors.Count != 0)
        {
            throw new RadioReviewCopyException("radio analysis must contain all tracks with zero decoder errors");
        }

        var analysisByPath = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var track in tracks)
        {
            if (track is JsonObject row && row["path"] is JsonValue pathValue && pathValue.GetValueKind() == JsonValueKind.String)
            {
                analysisByPath[pathValue.GetValue<string>()] = row;
            }
        }

        if (!analysisByPath.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(library.Assets.Select(asset => asset.RelativePath)))
        {
            throw new RadioReviewCopyException("radio analysis track set does not match the inventory");
        }

        var sourceHashesBefore = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in library.Assets)
        {
            var actual = Sha256(asset.SourcePath);
            sourceHashesBefore[asset.RelativePath] = actual;
            if (!string.Equals(actual, asset.ExpectedSha256, StringComparison.Ordinal))
            {
                throw new RadioReviewCopyException("source does not match inventory: " + asset.RelativePath);
            }

            if (!IsString(analysisByPath[asset.RelativePath]["sourceSha256"], asset.ExpectedSha256))
            {
                throw new RadioReviewCopyException("analysis does not match inventory: " + asset.RelativePath);
            }
        }

        var ffmpegVersion = ToolVersion(ffmpeg, runner);
        var ffprobeVersion = ToolVersion(ffprobe, runner);
        var resolvedOutputRoot = RequireOutputRoot(repositoryRoot, outputRoot);
        var finalDirectory = Path.Combine(resolvedOutputRoot, stationId);
        if (Exists(finalDirectory) && !replace)
        {
            throw new RadioReviewCopyException("review set already exists; pass --replace to replace it: " + finalDirectory);
        }

        if (Exists(finalDirectory))
        {
            ValidateReplaceTarget(finalDirectory, stationId);
        }

        var outputNames = selected.Select(asset => OutputName(asset.RelativePath)).ToArray();
        if (outputNames.Distinct(StringComparer.Ordinal).Count() != outputNames.Length)
        {
            throw new RadioReviewCopyException("station review-copy file names collide");
        }

        var stagingDirectory = Path.Combine(
            resolvedOutputRoot,
            "." + stationId + ".staging." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var results = new List<JsonObject>(selected.Length);
            var finished = new int[1];
            RadioReviewCopyException? failure = null;
            var gate = new object();
            void Record(RadioSourceAsset asset, JsonObject row)
            {
                int completed;
                lock (gate)
                {
                    if (failure is not null)
                    {
                        return;
                    }

                    results.Add(row);
                    finished[0]++;
                    completed = finished[0];
                }

                progress?.Invoke(
                    "Prepared "
                    + completed.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + selected.Length.ToString(CultureInfo.InvariantCulture)
                    + ": "
                    + asset.RelativePath);
            }

            void Fail(RadioSourceAsset asset, Exception exception)
            {
                lock (gate)
                {
                    failure ??= new RadioReviewCopyException(
                        "review copy failed for " + asset.RelativePath + ": " + exception.Message);
                }
            }

            if (workers == 1)
            {
                foreach (var asset in selected)
                {
                    try
                    {
                        Record(asset, PrepareTrack(asset, analysisByPath[asset.RelativePath], stagingDirectory, ffmpeg, ffprobe, timeoutSeconds, runner));
                    }
                    catch (Exception exception) when (exception is RadioReviewCopyException or RadioAudioAnalysisException)
                    {
                        Fail(asset, exception);
                        break;
                    }
                }
            }
            else
            {
                Parallel.ForEach(
                    selected,
                    new ParallelOptions { MaxDegreeOfParallelism = workers },
                    (asset, state) =>
                    {
                        lock (gate)
                        {
                            if (failure is not null)
                            {
                                state.Stop();
                                return;
                            }
                        }

                        try
                        {
                            Record(
                                asset,
                                PrepareTrack(
                                    asset,
                                    analysisByPath[asset.RelativePath],
                                    stagingDirectory,
                                    ffmpeg,
                                    ffprobe,
                                    timeoutSeconds,
                                    runner));
                        }
                        catch (Exception exception) when (exception is RadioReviewCopyException or RadioAudioAnalysisException)
                        {
                            Fail(asset, exception);
                            state.Stop();
                        }
                    });
            }

            if (failure is not null)
            {
                throw failure;
            }

            results.Sort(static (left, right) => string.CompareOrdinal(
                left["sourcePath"]!.GetValue<string>(),
                right["sourcePath"]!.GetValue<string>()));
            var modifiedSources = new List<string>();
            foreach (var asset in library.Assets)
            {
                if (!string.Equals(Sha256(asset.SourcePath), sourceHashesBefore[asset.RelativePath], StringComparison.Ordinal))
                {
                    modifiedSources.Add(asset.RelativePath);
                }
            }

            modifiedSources.Sort(StringComparer.Ordinal);
            var technicalPassCount = results.Count(row => row["technicalPass"]!.GetValue<bool>());
            var technicalPass = technicalPassCount == results.Count && modifiedSources.Count == 0;
            var manifest = BuildManifest(
                stationId,
                technicalPass,
                modifiedSources,
                ffmpegVersion,
                ffprobeVersion,
                workers,
                timeoutSeconds,
                createdUtc,
                library,
                analysisFile.Sha256,
                SourceSetSha256(library.Assets),
                results);
            WriteManifest(Path.Combine(stagingDirectory, ManifestName), manifest);
            string? backupDirectory = null;
            if (Exists(finalDirectory))
            {
                backupDirectory = Path.Combine(resolvedOutputRoot, "." + stationId + ".backup." + Guid.NewGuid().ToString("N"));
                Directory.Move(finalDirectory, backupDirectory);
            }

            try
            {
                Directory.Move(stagingDirectory, finalDirectory);
            }
            catch
            {
                if (backupDirectory is not null && Directory.Exists(backupDirectory) && !Exists(finalDirectory))
                {
                    Directory.Move(backupDirectory, finalDirectory);
                }

                throw;
            }

            if (backupDirectory is not null && Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }

            return new PreparedStation(finalDirectory, manifest);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static JsonObject PrepareTrack(
        RadioSourceAsset asset,
        JsonObject analysis,
        string stagingDirectory,
        string ffmpeg,
        string ffprobe,
        int timeoutSeconds,
        RadioAudioAnalysisCheck.RadioToolRunner runner)
    {
        if (!string.Equals(Sha256(asset.SourcePath), StringOf(analysis["sourceSha256"]), StringComparison.Ordinal))
        {
            throw new RadioReviewCopyException("source no longer matches analysis evidence: " + asset.RelativePath);
        }

        var trim = ComputeTrimPlan(
            RequireFinite(analysis["durationSeconds"], "duration"),
            RequireFinite(analysis["leadingSilenceSeconds"], "leading silence"),
            RequireFinite(analysis["trailingSilenceSeconds"], "trailing silence"));
        if (!TryGetInt64(analysis["sampleRateHz"], out var sampleRate) || sampleRate is < 1 or > int.MaxValue)
        {
            throw new RadioReviewCopyException("analysis sample rate is not an integer: " + asset.RelativePath);
        }

        if (!TryGetInt64(analysis["channels"], out var channels) || channels is < 1 or > int.MaxValue)
        {
            throw new RadioReviewCopyException("analysis channel count is not an integer: " + asset.RelativePath);
        }

        var outputName = OutputName(asset.RelativePath);
        var outputPath = Path.Combine(stagingDirectory, outputName);
        var adjustments = new JsonArray();
        LoudnormSummary firstPass = null!;
        LoudnormSummary secondPass = null!;
        MediaProbe media = default;
        LoudnessMeasurement measurement = default;
        SilenceMeasurement silence = default;
        var failures = new List<string>();
        var attempt = 0;
        for (attempt = 1; attempt <= MaximumNormalizationAttempts; attempt++)
        {
            var trimFilter = BuildTrimFilter(trim);
            firstPass = ParseLoudnorm(Invoke(
                runner,
                ffmpeg,
                FirstPassArguments(asset.SourcePath, trimFilter),
                timeoutSeconds).StandardError);
            var secondPassFilter = BuildSecondPassFilter(trimFilter, firstPass, (int)sampleRate);
            if (File.Exists(outputPath) || Directory.Exists(outputPath))
            {
                if (Directory.Exists(outputPath) && !File.Exists(outputPath))
                {
                    throw new RadioReviewCopyException("ffmpeg did not create a regular review copy: " + outputName);
                }

                File.Delete(outputPath);
            }

            secondPass = ParseLoudnorm(Invoke(
                runner,
                ffmpeg,
                SecondPassArguments(asset.SourcePath, secondPassFilter, outputPath),
                timeoutSeconds).StandardError);
            if (IsReparse(outputPath) || !File.Exists(outputPath))
            {
                throw new RadioReviewCopyException("ffmpeg did not create a regular review copy: " + outputName);
            }

            var probed = RadioAudioAnalysisCheck.ParseFfprobe(Invoke(
                runner,
                ffprobe,
                ProbeArguments(outputPath),
                timeoutSeconds).StandardOutput);
            var decoded = Invoke(runner, ffmpeg, MeasureArguments(outputPath), timeoutSeconds).StandardError;
            measurement = RadioAudioAnalysisCheck.ParseFfmpeg(decoded);
            silence = RadioAudioAnalysisCheck.ParseSilence(decoded, probed.DurationSeconds);
            media = probed;
            failures = ValidateReviewMeasurement(
                media,
                measurement,
                silence,
                (int)channels,
                (int)sampleRate,
                trim.ExpectedOutputDurationSeconds);
            var correction = ComputeEdgeCorrection(silence.LeadingSilenceSeconds, silence.TrailingSilenceSeconds);
            var nonEdgeFailures = failures.Where(failure => !failure.Contains("silence exceeds", StringComparison.Ordinal)).ToArray();
            var correctionRequired = (correction.AdditionalStartSeconds > 0.0 || correction.AdditionalEndSeconds > 0.0)
                && nonEdgeFailures.Length == 0;
            if (!correctionRequired || attempt == MaximumNormalizationAttempts)
            {
                break;
            }

            var nextStart = trim.StartSeconds + correction.AdditionalStartSeconds;
            var nextEnd = trim.EndSeconds - correction.AdditionalEndSeconds;
            var nextDuration = nextEnd - nextStart;
            if (nextDuration < RadioAudioAnalysisCheck.MinimumDurationSeconds)
            {
                failures.Add("post-normalization edge correction would produce an undersized review copy");
                break;
            }

            adjustments.Add(new JsonObject
            {
                ["attempt"] = attempt,
                ["measuredLeadingSilenceSeconds"] = silence.LeadingSilenceSeconds,
                ["measuredTrailingSilenceSeconds"] = silence.TrailingSilenceSeconds,
                ["additionalStartSeconds"] = correction.AdditionalStartSeconds,
                ["additionalEndSeconds"] = correction.AdditionalEndSeconds,
            });
            trim = trim with
            {
                StartSeconds = Round(nextStart, 6),
                EndSeconds = Round(nextEnd, 6),
                ExpectedOutputDurationSeconds = Round(nextDuration, 6),
            };
        }

        if (!string.Equals(Sha256(asset.SourcePath), StringOf(analysis["sourceSha256"]), StringComparison.Ordinal))
        {
            failures.Add("source changed while preparing the review copy");
        }

        var failureArray = new JsonArray();
        foreach (var failure in failures)
        {
            failureArray.Add(failure);
        }

        return new JsonObject
        {
            ["assetId"] = asset.AssetId,
            ["stationId"] = asset.StationId,
            ["sourcePath"] = asset.RelativePath,
            ["sourceBytes"] = asset.ExpectedBytes,
            ["sourceSha256"] = asset.ExpectedSha256,
            ["outputFile"] = outputName,
            ["outputBytes"] = new FileInfo(outputPath).Length,
            ["outputSha256"] = Sha256(outputPath),
            ["trim"] = TrimJson(trim),
            ["normalizationAttemptCount"] = attempt,
            ["postNormalizationEdgeTrimAdjustments"] = adjustments,
            ["firstPass"] = LoudnormJson(firstPass),
            ["secondPass"] = LoudnormJson(secondPass),
            ["media"] = MediaJson(media),
            ["measurement"] = MeasurementJson(measurement),
            ["silence"] = SilenceJson(silence),
            ["technicalPass"] = failures.Count == 0,
            ["failures"] = failureArray,
        };
    }

    private static JsonObject BuildManifest(
        string stationId,
        bool technicalPass,
        List<string> modifiedSources,
        string ffmpegVersion,
        string ffprobeVersion,
        int workers,
        int timeoutSeconds,
        DateTimeOffset createdUtc,
        RadioLibrary library,
        string analysisSha256,
        string sourceSetSha256,
        List<JsonObject> results)
    {
        var modified = new JsonArray();
        foreach (var path in modifiedSources)
        {
            modified.Add(path);
        }

        var copies = new JsonArray();
        long sourceBytes = 0;
        long outputBytes = 0;
        var technicalPassCount = 0;
        var linearCount = 0;
        var dynamicCount = 0;
        foreach (var result in results)
        {
            copies.Add(result);
            sourceBytes += result["sourceBytes"]!.GetValue<long>();
            outputBytes += result["outputBytes"]!.GetValue<long>();
            if (result["technicalPass"]!.GetValue<bool>())
            {
                technicalPassCount++;
            }

            var normalizationType = result["secondPass"]!["normalizationType"]!.GetValue<string>();
            if (normalizationType == "linear")
            {
                linearCount++;
            }
            else if (normalizationType == "dynamic")
            {
                dynamicCount++;
            }
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = ReviewSetKind,
            ["createdUtc"] = RadioAudioAnalysisCheck.FormatUtc(createdUtc),
            ["stationId"] = stationId,
            ["technicalPass"] = technicalPass,
            ["releaseApproved"] = false,
            ["sourceReplacementApproved"] = false,
            ["exportEligibilityChanged"] = false,
            ["humanListeningRequired"] = true,
            ["humanListeningStatus"] = "pending",
            ["sourceBytesModified"] = modifiedSources.Count > 0,
            ["modifiedSourcePaths"] = modified,
            ["toolchain"] = new JsonObject
            {
                ["ffmpeg"] = ffmpegVersion,
                ["ffprobe"] = ffprobeVersion,
                ["workers"] = workers,
                ["perTrackTimeoutSeconds"] = timeoutSeconds,
            },
            ["policy"] = new JsonObject
            {
                ["targetIntegratedLufs"] = RadioAudioAnalysisCheck.TargetIntegratedLufs,
                ["loudnessToleranceLu"] = RadioAudioAnalysisCheck.LoudnessToleranceLu,
                ["maximumTruePeakDbtp"] = RadioAudioAnalysisCheck.MaximumTruePeakDbtp,
                ["normalizationLraTargetLu"] = NormalizationLraTargetLu,
                ["maximumLeadingSilenceSeconds"] = RadioAudioAnalysisCheck.MaximumLeadingSilenceSeconds,
                ["maximumTrailingSilenceSeconds"] = RadioAudioAnalysisCheck.MaximumTrailingSilenceSeconds,
                ["maximumInternalSilenceSeconds"] = RadioAudioAnalysisCheck.MaximumInternalSilenceSeconds,
                ["retainedEdgeSilenceSeconds"] = EdgeSilenceRetainSeconds,
                ["postNormalizationEdgeCorrectionMarginSeconds"] = EdgeCorrectionMarginSeconds,
                ["maximumNormalizationAttempts"] = MaximumNormalizationAttempts,
                ["outputCodec"] = OutputCodec,
                ["outputSampleFormat"] = OutputSampleFormat,
            },
            ["inputs"] = new JsonObject
            {
                ["inventorySha256"] = library.InventorySha256,
                ["curationSha256"] = library.CurationSha256,
                ["inventoryPolicySha256"] = library.InventoryPolicySha256,
                ["curationDecisionStatus"] = library.CurationDecisionStatus,
                ["analysisEvidenceSha256"] = analysisSha256,
                ["radioSourceSetSha256"] = sourceSetSha256,
            },
            ["summary"] = new JsonObject
            {
                ["trackCount"] = results.Count,
                ["technicalPassCount"] = technicalPassCount,
                ["technicalFailureCount"] = results.Count - technicalPassCount,
                ["sourceBytes"] = sourceBytes,
                ["outputBytes"] = outputBytes,
                ["linearNormalizationCount"] = linearCount,
                ["dynamicNormalizationCount"] = dynamicCount,
            },
            ["reviewCopies"] = copies,
        };
    }

    private static string[] FirstPassArguments(string sourcePath, string trimFilter) =>
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
        trimFilter
            + ",loudnorm=I="
            + Number(RadioAudioAnalysisCheck.TargetIntegratedLufs)
            + ":TP="
            + Number(RadioAudioAnalysisCheck.MaximumTruePeakDbtp)
            + ":LRA="
            + Number(NormalizationLraTargetLu)
            + ":print_format=json",
        "-f",
        "null",
        "-",
    ];

    private static string[] SecondPassArguments(string sourcePath, string filter, string outputPath) =>
    [
        "-nostdin",
        "-hide_banner",
        "-nostats",
        "-loglevel",
        "info",
        "-fflags",
        "+bitexact",
        "-i",
        sourcePath,
        "-map",
        "0:a:0",
        "-map_metadata",
        "-1",
        "-map_chapters",
        "-1",
        "-vn",
        "-af",
        filter,
        "-sample_fmt",
        OutputSampleFormat,
        "-c:a",
        OutputCodec,
        "-compression_level",
        "8",
        "-flags:a",
        "+bitexact",
        "-threads",
        "1",
        outputPath,
    ];

    private static string[] ProbeArguments(string path) =>
    [
        "-v",
        "error",
        "-select_streams",
        "a",
        "-show_entries",
        "format=duration,bit_rate,format_name:stream=codec_name,sample_rate,channels,channel_layout,duration,bit_rate",
        "-of",
        "json",
        path,
    ];

    private static string[] MeasureArguments(string path) =>
    [
        "-nostdin",
        "-hide_banner",
        "-nostats",
        "-loglevel",
        "info",
        "-i",
        path,
        "-map",
        "0:a:0",
        "-af",
        "ebur128=peak=true:framelog=verbose,volumedetect,silencedetect=noise=-60dB:d=1",
        "-f",
        "null",
        "-",
    ];

    private static string ToolVersion(string executable, RadioAudioAnalysisCheck.RadioToolRunner runner)
    {
        var result = Invoke(runner, executable, ["-version"], 30);
        var text = result.StandardOutput.Length > 0 ? result.StandardOutput : result.StandardError;
        if (text.Length == 0)
        {
            throw new RadioReviewCopyException("tool version check returned no output: " + executable);
        }

        var newline = text.IndexOfAny(['\r', '\n']);
        var first = newline < 0 ? text : text[..newline];
        return first.Trim();
    }

    private static RadioToolResult Invoke(
        RadioAudioAnalysisCheck.RadioToolRunner runner,
        string executable,
        IReadOnlyList<string> arguments,
        int timeoutSeconds)
    {
        var result = runner(executable, arguments, timeoutSeconds);
        if (result.TimedOut)
        {
            throw new RadioReviewCopyException(
                "tool execution failed: "
                + executable
                + ": timed out after "
                + timeoutSeconds.ToString(CultureInfo.InvariantCulture)
                + " seconds");
        }

        if (result.ExitCode != 0)
        {
            var diagnostic = string.IsNullOrEmpty(result.StandardError) ? result.StandardOutput : result.StandardError;
            var stripped = diagnostic.Trim();
            if (stripped.Length > 1000)
            {
                stripped = stripped[^1000..];
            }

            throw new RadioReviewCopyException(
                "tool failed with exit code "
                + result.ExitCode.ToString(CultureInfo.InvariantCulture)
                + ": "
                + executable
                + ": "
                + stripped);
        }

        return result;
    }

    private static bool InputsMatch(JsonNode? node, RadioLibrary library)
    {
        if (node is not JsonObject inputs || inputs.Count != 4)
        {
            return false;
        }

        return IsString(inputs["inventorySha256"], library.InventorySha256)
            && IsString(inputs["curationSha256"], library.CurationSha256)
            && IsString(inputs["inventoryPolicySha256"], library.InventoryPolicySha256)
            && IsString(inputs["curationDecisionStatus"], library.CurationDecisionStatus);
    }

    private static string OutputName(string relativePath) =>
        Path.GetFileNameWithoutExtension(relativePath.Replace('/', Path.DirectorySeparatorChar)) + ".review.flac";

    private static string SourceSetSha256(IReadOnlyList<RadioSourceAsset> assets)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < assets.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"path\":");
            builder.Append(JsonSerializer.Serialize(assets[index].RelativePath));
            builder.Append(",\"sha256\":");
            builder.Append(JsonSerializer.Serialize(assets[index].ExpectedSha256));
            builder.Append('}');
        }

        builder.Append(']');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void WriteManifest(string path, JsonObject manifest)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, RenderOptions);
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            Array.Resize(ref bytes, bytes.Length + 1);
            bytes[^1] = (byte)'\n';
        }

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static ParsedJson ReadObject(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            if (!File.Exists(path) || Directory.Exists(path) || IsReparse(path))
            {
                throw new RadioReviewCopyException(
                    "JSON input is unreadable: " + name + ": radio review JSON input must be a regular file");
            }

            var info = new FileInfo(path);
            if (info.Length > MaximumJsonBytes)
            {
                throw new RadioReviewCopyException(
                    "JSON input exceeds "
                    + MaximumJsonBytes.ToString(CultureInfo.InvariantCulture)
                    + " bytes: "
                    + name);
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > MaximumJsonBytes)
            {
                throw new RadioReviewCopyException(
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
                throw new RadioReviewCopyException("JSON input must contain an object: " + name);
            }

            return new ParsedJson(obj, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        catch (RadioReviewCopyException)
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
            throw new RadioReviewCopyException(
                "JSON input is unreadable: " + name + ": " + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    private static string Sha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RadioReviewCopyException(
                "source read failed: " + path + ": " + StrictJsonFile.SingleLine(exception.Message));
        }
    }

    private static bool HasLoudnormFields(JsonElement element)
    {
        foreach (var field in LoudnormFields)
        {
            if (!element.TryGetProperty(field, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static double Finite(JsonNode? node, string label) => Finite(RequireFinite(node, label), label);

    private static double Finite(double value, string label)
    {
        if (!double.IsFinite(value))
        {
            throw new RadioReviewCopyException(label + " is not finite");
        }

        return value;
    }

    private static double RequireFinite(JsonNode? node, string label)
    {
        if (node is not JsonValue value)
        {
            throw new RadioReviewCopyException(label + " is not numeric");
        }

        if (value.TryGetValue<double>(out var number))
        {
            return Finite(number, label);
        }

        if (value.TryGetValue<string>(out var text))
        {
            if (text.Equals("inf", StringComparison.OrdinalIgnoreCase)
                || text.Equals("-inf", StringComparison.OrdinalIgnoreCase)
                || text.Equals("+inf", StringComparison.OrdinalIgnoreCase))
            {
                throw new RadioReviewCopyException(label + " is not finite");
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return Finite(number, label);
            }
        }

        throw new RadioReviewCopyException(label + " is not numeric");
    }

    private static bool TryGetInt64(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue json
            || !json.TryGetValue<JsonElement>(out var element)
            || element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = element.GetRawText();
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && string.Equals(raw, value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static bool IsInteger(JsonNode? node, long expected) =>
        TryGetInt64(node, out var value) && value == expected;

    private static bool IsString(JsonNode? node, string expected) =>
        node is JsonValue value
        && value.GetValueKind() == JsonValueKind.String
        && string.Equals(value.GetValue<string>(), expected, StringComparison.Ordinal);

    private static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static JsonObject TrimJson(TrimPlan trim) => new()
    {
        ["sourceDurationSeconds"] = trim.SourceDurationSeconds,
        ["startSeconds"] = trim.StartSeconds,
        ["endSeconds"] = trim.EndSeconds,
        ["expectedOutputDurationSeconds"] = trim.ExpectedOutputDurationSeconds,
        ["retainedEdgeSilenceSeconds"] = trim.RetainedEdgeSilenceSeconds,
    };

    private static JsonObject LoudnormJson(LoudnormSummary summary) => new()
    {
        ["inputIntegratedLufs"] = summary.InputIntegratedLufs,
        ["inputTruePeakDbtp"] = summary.InputTruePeakDbtp,
        ["inputLoudnessRangeLu"] = summary.InputLoudnessRangeLu,
        ["inputThresholdLufs"] = summary.InputThresholdLufs,
        ["outputIntegratedLufs"] = summary.OutputIntegratedLufs,
        ["outputTruePeakDbtp"] = summary.OutputTruePeakDbtp,
        ["outputLoudnessRangeLu"] = summary.OutputLoudnessRangeLu,
        ["outputThresholdLufs"] = summary.OutputThresholdLufs,
        ["targetOffsetDb"] = summary.TargetOffsetDb,
        ["normalizationType"] = summary.NormalizationType,
    };

    private static JsonObject MediaJson(MediaProbe media) => new()
    {
        ["codec"] = media.Codec,
        ["container"] = media.Container,
        ["sampleRateHz"] = media.SampleRateHz,
        ["channels"] = media.Channels,
        ["channelLayout"] = media.ChannelLayout,
        ["durationSeconds"] = media.DurationSeconds,
        ["bitRateBps"] = media.BitRateBps,
    };

    private static JsonObject MeasurementJson(LoudnessMeasurement measurement) => new()
    {
        ["integratedLufs"] = measurement.IntegratedLufs,
        ["loudnessRangeLu"] = measurement.LoudnessRangeLu,
        ["truePeakDbtp"] = measurement.TruePeakDbtp,
        ["decodedSampleCount"] = measurement.DecodedSampleCount,
        ["meanVolumeDbfs"] = measurement.MeanVolumeDbfs,
        ["samplePeakDbfs"] = measurement.SamplePeakDbfs,
        ["highestBucketSampleCount"] = measurement.HighestBucketSampleCount,
    };

    private static JsonObject SilenceJson(SilenceMeasurement silence) => new()
    {
        ["silenceIntervalCount"] = silence.SilenceIntervalCount,
        ["totalSilenceSeconds"] = silence.TotalSilenceSeconds,
        ["leadingSilenceSeconds"] = silence.LeadingSilenceSeconds,
        ["trailingSilenceSeconds"] = silence.TrailingSilenceSeconds,
        ["maximumInternalSilenceSeconds"] = silence.MaximumInternalSilenceSeconds,
    };

    private static string Number(double value)
    {
        var rounded = Math.Round(value, 6, MidpointRounding.ToEven);
        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static double Round(double value, int digits) =>
        Math.Round(value, digits, MidpointRounding.ToEven);

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

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
}

internal sealed class LoudnormSummary
{
    internal double InputIntegratedLufs { get; init; }

    internal double InputTruePeakDbtp { get; init; }

    internal double InputLoudnessRangeLu { get; init; }

    internal double InputThresholdLufs { get; init; }

    internal double OutputIntegratedLufs { get; init; }

    internal double OutputTruePeakDbtp { get; init; }

    internal double OutputLoudnessRangeLu { get; init; }

    internal double OutputThresholdLufs { get; init; }

    internal double TargetOffsetDb { get; init; }

    internal string NormalizationType { get; set; } = string.Empty;
}

internal readonly record struct TrimPlan(
    double SourceDurationSeconds,
    double StartSeconds,
    double EndSeconds,
    double ExpectedOutputDurationSeconds,
    double RetainedEdgeSilenceSeconds);

internal readonly record struct EdgeCorrection(double AdditionalStartSeconds, double AdditionalEndSeconds);

internal readonly record struct PreparedStation(string Directory, JsonObject Manifest);

internal sealed class RadioReviewCopyException : InvalidOperationException
{
    internal RadioReviewCopyException(string message)
        : base(message)
    {
    }
}
