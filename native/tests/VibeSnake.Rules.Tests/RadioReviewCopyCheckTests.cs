using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public class RadioReviewCopyCheckTests
{
    private const string Policy = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Loudnorm_trim_and_measurement_match_the_python_contract()
    {
        var summary = RadioReviewCopyCheck.ParseLoudnorm(
            """
            [Parsed_loudnorm_0] {
                "input_i" : "-13.20",
                "input_tp" : "0.30",
                "input_lra" : "4.10",
                "input_thresh" : "-23.60",
                "output_i" : "-18.00",
                "output_tp" : "-1.00",
                "output_lra" : "4.00",
                "output_thresh" : "-28.40",
                "normalization_type" : "dynamic",
                "target_offset" : "0.00"
            }
            """);
        Assert.Equal(-13.2, summary.InputIntegratedLufs);
        Assert.Equal(0.3, summary.InputTruePeakDbtp);
        Assert.Equal(4.1, summary.InputLoudnessRangeLu);
        Assert.Equal(-23.6, summary.InputThresholdLufs);
        Assert.Equal(-18.0, summary.OutputIntegratedLufs);
        Assert.Equal(-1.0, summary.OutputTruePeakDbtp);
        Assert.Equal(4.0, summary.OutputLoudnessRangeLu);
        Assert.Equal(-28.4, summary.OutputThresholdLufs);
        Assert.Equal(0.0, summary.TargetOffsetDb);
        Assert.Equal("dynamic", summary.NormalizationType);

        var duplicate = Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ParseLoudnorm(
            """{"input_i":"-18","input_tp":"-1","input_lra":"1","input_thresh":"-20","output_i":"-18","output_tp":"-1","output_lra":"1","output_thresh":"-28","target_offset":"0","normalization_type":"linear"}{"input_i":"-18","input_tp":"-1","input_lra":"1","input_thresh":"-20","output_i":"-18","output_tp":"-1","output_lra":"1","output_thresh":"-28","target_offset":"0","normalization_type":"linear"}"""));
        Assert.Contains("exactly one complete loudnorm", duplicate.Message, StringComparison.Ordinal);
        var infinite = Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ParseLoudnorm(
            """{"input_i":"-inf","input_tp":"0","input_lra":"1","input_thresh":"-20","output_i":"-18","output_tp":"-1","output_lra":"1","output_thresh":"-28","target_offset":"0","normalization_type":"linear"}"""));
        Assert.Contains("not finite", infinite.Message, StringComparison.Ordinal);
        var invalidType = Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ParseLoudnorm(
            """{"input_i":"-18","input_tp":"-1","input_lra":"1","input_thresh":"-20","output_i":"-18","output_tp":"-1","output_lra":"1","output_thresh":"-28","target_offset":"0","normalization_type":"other"}"""));
        Assert.Contains("normalization type", invalidType.Message, StringComparison.Ordinal);
        Assert.Contains(
            "exactly one complete loudnorm",
            Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ParseLoudnorm("{}")).Message,
            StringComparison.Ordinal);

        var plan = RadioReviewCopyCheck.ComputeTrimPlan(270.03, 7.82, 3.64);
        Assert.Equal(270.03, plan.SourceDurationSeconds, 6);
        Assert.Equal(7.57, plan.StartSeconds, 6);
        Assert.Equal(266.64, plan.EndSeconds, 6);
        Assert.Equal(259.07, plan.ExpectedOutputDurationSeconds, 6);
        Assert.Equal(0.25, plan.RetainedEdgeSilenceSeconds, 6);
        Assert.Equal("atrim=start=7.57:end=266.64,asetpts=PTS-STARTPTS", RadioReviewCopyCheck.BuildTrimFilter(plan));
        Assert.Contains(
            "invalid edge-silence",
            Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ComputeTrimPlan(30.0, 15.0, 15.0)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "undersized",
            Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ComputeTrimPlan(35.0, 3.0, 3.0)).Message,
            StringComparison.Ordinal);

        var filter = RadioReviewCopyCheck.BuildSecondPassFilter(
            "atrim=start=0:end=100,asetpts=PTS-STARTPTS",
            summary,
            44100);
        Assert.Equal(
            "atrim=start=0:end=100,asetpts=PTS-STARTPTS,loudnorm=I=-18:TP=-1:LRA=50:measured_I=-13.2:measured_LRA=4.1:measured_TP=0.3:measured_thresh=-23.6:offset=0:linear=true:print_format=json,aresample=44100",
            filter);
        var correction = RadioReviewCopyCheck.ComputeEdgeCorrection(2.061111, 1.5);
        Assert.Equal(0.161111, correction.AdditionalStartSeconds, 6);
        Assert.Equal(0.0, correction.AdditionalEndSeconds, 6);

        var passing = RadioReviewCopyCheck.ValidateReviewMeasurement(
            new MediaProbe("flac", "flac", 44100, 2, "stereo", 100.01, 8),
            new LoudnessMeasurement(-18.0, 4.0, -1.0, 10, -18.0, -1.0, 0),
            new SilenceMeasurement(2, 0.5, 0.25, 0.25, 0.0),
            2,
            44100,
            100.0);
        Assert.Empty(passing);
        var failures = RadioReviewCopyCheck.ValidateReviewMeasurement(
            new MediaProbe("mp3", "mp3", 48000, 1, "mono", 90.0, 8),
            new LoudnessMeasurement(-10.0, 4.0, 0.0, 10, -10.0, 0.0, 0),
            new SilenceMeasurement(3, 12.0, 3.0, 3.0, 6.0),
            2,
            44100,
            100.0);
        Assert.Equal(9, failures.Count);
    }

    [Fact]
    public void Replace_target_and_output_root_fail_closed()
    {
        var root = TempDirectory();
        var outside = TempDirectory();
        try
        {
            var station = Path.Combine(outside, "station");
            Directory.CreateDirectory(station);
            var missing = Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ValidateReplaceTarget(station, "station"));
            Assert.Contains("without its manifest", missing.Message, StringComparison.Ordinal);
            File.WriteAllText(
                Path.Combine(station, "review-copy-manifest.json"),
                "{\"kind\":\"foreign\",\"schemaVersion\":1,\"stationId\":\"station\"}\n");
            var foreign = Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ValidateReplaceTarget(station, "station"));
            Assert.Contains("foreign manifest", foreign.Message, StringComparison.Ordinal);
            File.WriteAllText(
                Path.Combine(station, "review-copy-manifest.json"),
                "{\"kind\":\"vibesnake-radio-review-copy-set-v1\",\"schemaVersion\":1,\"stationId\":\"station\"}\n");
            RadioReviewCopyCheck.ValidateReplaceTarget(station, "station");
            Assert.Contains(
                "non-directory",
                Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.ValidateReplaceTarget(Path.Combine(outside, "missing"), "station")).Message,
                StringComparison.Ordinal);

            var allowed = RadioReviewCopyCheck.RequireOutputRoot(root, Path.Combine(root, "TestResults", "radio-review"));
            Assert.True(Directory.Exists(allowed));
            var external = RadioReviewCopyCheck.RequireOutputRoot(root, Path.Combine(outside, "external-review"));
            Assert.True(Directory.Exists(external));
            var archive = RadioReviewCopyCheck.RequireOutputRoot(root, Path.Combine(root, "archive", "radio-review"));
            Assert.True(Directory.Exists(archive));
            Assert.Contains(
                "TestResults or archive",
                Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.RequireOutputRoot(root, Path.Combine(root, "docs"))).Message,
                StringComparison.Ordinal);
            var file = Path.Combine(outside, "not-a-directory");
            File.WriteAllBytes(file, [1]);
            Assert.Contains(
                "regular directory",
                Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.RequireOutputRoot(root, file)).Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Prepare_publishes_a_pending_station_that_listening_can_rehash()
    {
        var root = TempDirectory();
        try
        {
            var library = WriteLibrary(root);
            var analysis = WriteAnalysis(root, library);
            var ffmpeg = Path.Combine(root, "ffmpeg-stub");
            var ffprobe = Path.Combine(root, "ffprobe-stub");
            File.WriteAllBytes(ffmpeg, []);
            File.WriteAllBytes(ffprobe, []);
            var output = Path.Combine(root, "TestResults", "radio-review");
            var tools = new ScriptedTools();
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = RepositoryCheckCommand.Run(
                ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe, workers: "2"),
                stdout,
                TextWriter.Synchronized(stderr),
                tools.Run);
            Assert.Equal(0, exit);
            Assert.Contains("station=the_bureau tracks=12 technical_pass=12 failures=0", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Prepared 12/12:", stderr.ToString(), StringComparison.Ordinal);
            Assert.Equal(12, tools.Progress);
            var manifestText = File.ReadAllText(Path.Combine(output, "the_bureau", "review-copy-manifest.json"));
            Assert.Contains("\"releaseApproved\": false", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"sourceReplacementApproved\": false", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"exportEligibilityChanged\": false", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"humanListeningStatus\": \"pending\"", manifestText, StringComparison.Ordinal);
            Assert.Contains("\"sourceBytesModified\": false", manifestText, StringComparison.Ordinal);
            Assert.Contains("noise=-60dB:d=1", tools.MeasureFilter, StringComparison.Ordinal);
            Assert.Contains("loudnorm=I=-18:TP=-1:LRA=50:print_format=json", tools.FirstFilter, StringComparison.Ordinal);
            Assert.Contains("\"-c:a\", \"flac\"", tools.SecondArguments, StringComparison.Ordinal);
            var verification = RadioListeningReviewCheck.VerifyInputs(
                root,
                Path.Combine(output, "the_bureau"),
                Path.Combine(output, "verify.json"));
            Assert.Equal("the_bureau", verification.StationId);
            Assert.Equal(12, verification.TrackCount);

            var again = new StringWriter();
            var blocked = RepositoryCheckCommand.Run(
                ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe),
                new StringWriter(),
                again,
                tools.Run);
            Assert.Equal(2, blocked);
            Assert.Contains("pass --replace", again.ToString(), StringComparison.Ordinal);
            File.WriteAllText(
                Path.Combine(output, "the_bureau", "review-copy-manifest.json"),
                "{\"kind\":\"foreign\",\"schemaVersion\":1,\"stationId\":\"the_bureau\"}\n");
            var foreign = new StringWriter();
            var foreignExit = RepositoryCheckCommand.Run(
                ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe, replace: "replace"),
                new StringWriter(),
                foreign,
                tools.Run);
            Assert.Equal(2, foreignExit);
            Assert.Contains("foreign manifest", foreign.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_records_corrections_failures_and_source_changes_without_approval()
    {
        var root = TempDirectory();
        try
        {
            var library = WriteLibrary(root);
            var analysis = WriteAnalysis(root, library, edgeTrack: true);
            var ffmpeg = Path.Combine(root, "ffmpeg-stub");
            var ffprobe = Path.Combine(root, "ffprobe-stub");
            File.WriteAllBytes(ffmpeg, []);
            File.WriteAllBytes(ffprobe, []);
            var output = Path.Combine(root, "TestResults", "radio-review");
            var tools = new ScriptedTools { EdgeMode = true };
            var stdout = new StringWriter();
            var exit = RepositoryCheckCommand.Run(
                ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe),
                stdout,
                new StringWriter(),
                tools.Run);
            Assert.Equal(1, exit);
            Assert.Contains("technical_pass=10 failures=2", stdout.ToString(), StringComparison.Ordinal);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "the_bureau", "review-copy-manifest.json")));
            Assert.False(manifest.RootElement.GetProperty("technicalPass").GetBoolean());
            Assert.False(manifest.RootElement.GetProperty("releaseApproved").GetBoolean());
            Assert.True(manifest.RootElement.GetProperty("sourceBytesModified").GetBoolean());
            var copies = manifest.RootElement.GetProperty("reviewCopies");
            JsonElement corrected = default;
            JsonElement undersized = default;
            JsonElement exhausted = default;
            foreach (var copy in copies.EnumerateArray())
            {
                var name = copy.GetProperty("outputFile").GetString();
                if (name == "track-000.review.flac")
                {
                    corrected = copy;
                }
                else if (name == "track-001.review.flac")
                {
                    undersized = copy;
                }
                else if (name == "track-002.review.flac")
                {
                    exhausted = copy;
                }
            }

            Assert.True(corrected.GetProperty("technicalPass").GetBoolean());
            Assert.Equal(2, corrected.GetProperty("normalizationAttemptCount").GetInt32());
            Assert.Equal(1, corrected.GetProperty("postNormalizationEdgeTrimAdjustments").GetArrayLength());
            Assert.False(undersized.GetProperty("technicalPass").GetBoolean());
            Assert.Contains(
                "post-normalization edge correction would produce an undersized review copy",
                undersized.GetProperty("failures").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(3, exhausted.GetProperty("normalizationAttemptCount").GetInt32());
            Assert.Equal(2, exhausted.GetProperty("postNormalizationEdgeTrimAdjustments").GetArrayLength());
            Assert.Contains(
                "audio/radio/track-090.mp3",
                manifest.RootElement.GetProperty("modifiedSourcePaths").EnumerateArray().Select(item => item.GetString()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Command_rejects_unsafe_invocation_without_publishing()
    {
        var root = TempDirectory();
        try
        {
            var library = WriteLibrary(root);
            var analysis = WriteAnalysis(root, library);
            var ffmpeg = Path.Combine(root, "ffmpeg-stub");
            var ffprobe = Path.Combine(root, "ffprobe-stub");
            File.WriteAllBytes(ffmpeg, []);
            File.WriteAllBytes(ffprobe, []);
            var output = Path.Combine(root, "TestResults", "radio-review");
            var usage = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(["radio-review"], new StringWriter(), usage));
            Assert.Contains("radio-review <repository-root> prepare", usage.ToString(), StringComparison.Ordinal);
            var badRoot = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ReviewArgs("bad\0root", library, analysis, output, ffmpeg, ffprobe),
                    new StringWriter(),
                    badRoot,
                    new ScriptedTools().Run));
            Assert.Contains("Repository root is invalid.", badRoot.ToString(), StringComparison.Ordinal);
            var badPath = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ReviewArgs(root, library with { Inventory = " " }, analysis, output, ffmpeg, ffprobe),
                    new StringWriter(),
                    badPath));
            Assert.Contains("Radio review path is invalid.", badPath.ToString(), StringComparison.Ordinal);
            var canonical = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe, workers: "08"),
                    new StringWriter(),
                    canonical));
            Assert.Contains("prepare <station>", canonical.ToString(), StringComparison.Ordinal);
            var range = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe, workers: "5"),
                    new StringWriter(),
                    range,
                    new ScriptedTools().Run));
            Assert.Contains("workers must be between 1 and 4", range.ToString(), StringComparison.Ordinal);
            var timeout = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe, timeout: "29"),
                    new StringWriter(),
                    timeout,
                    new ScriptedTools().Run));
            Assert.Contains("timeout-seconds must be between 30 and 1800", timeout.ToString(), StringComparison.Ordinal);
            var missing = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ReviewArgs(root, library, analysis, output, "missing-ffmpeg-tool", ffprobe),
                    new StringWriter(),
                    missing));
            Assert.Contains("ffmpeg and ffprobe must both be available", missing.ToString(), StringComparison.Ordinal);
            var station = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe, station: "TheBureau"),
                    new StringWriter(),
                    station,
                    new ScriptedTools().Run));
            Assert.Contains("lowercase underscore identifier", station.ToString(), StringComparison.Ordinal);
            var docs = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ReviewArgs(root, library, analysis, Path.Combine(root, "docs"), ffmpeg, ffprobe),
                    new StringWriter(),
                    docs,
                    new ScriptedTools().Run));
            Assert.Contains("TestResults or archive", docs.ToString(), StringComparison.Ordinal);
            var version = new StringWriter();
            var versionExit = RepositoryCheckCommand.Run(
                ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe),
                new StringWriter(),
                version,
                new ScriptedTools { VersionText = string.Empty, VersionError = string.Empty }.Run);
            Assert.Equal(2, versionExit);
            Assert.Contains("tool version check returned no output", version.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(output, "the_bureau")));

            var natural = new StringWriter();
            var naturalExit = RepositoryCheckCommand.Run(
                ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe),
                new StringWriter(),
                natural);
            Assert.Equal(2, naturalExit);
            Assert.Contains("Radio review-copy preparation failed:", natural.ToString(), StringComparison.Ordinal);

            var collision = WriteLibrary(root, collide: true);
            var collisionAnalysis = WriteAnalysis(root, collision);
            var collided = Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.PrepareStation(
                root,
                collision.Inventory,
                collision.Curation,
                collisionAnalysis,
                output,
                "the_bureau",
                ffmpeg,
                ffprobe,
                1,
                30,
                false,
                new ScriptedTools().Run,
                DateTimeOffset.UnixEpoch,
                null));
            Assert.Contains("file names collide", collided.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prepare_rejects_untrusted_evidence_tools_and_station_bounds()
    {
        var root = TempDirectory();
        try
        {
            var library = WriteLibrary(root);
            var analysis = WriteAnalysis(root, library);
            var ffmpeg = Path.Combine(root, "ffmpeg-stub");
            var ffprobe = Path.Combine(root, "ffprobe-stub");
            File.WriteAllBytes(ffmpeg, []);
            File.WriteAllBytes(ffprobe, []);
            var output = Path.Combine(root, "TestResults", "radio-review");

            void Expect(string message, RadioAudioAnalysisCheck.RadioToolRunner? runner = null, int workers = 1, int timeout = 30)
            {
                var error = Assert.Throws<RadioReviewCopyException>(() => RadioReviewCopyCheck.PrepareStation(
                    root,
                    library.Inventory,
                    library.Curation,
                    analysis,
                    output,
                    "the_bureau",
                    ffmpeg,
                    ffprobe,
                    workers,
                    timeout,
                    false,
                    runner ?? new ScriptedTools().Run,
                    DateTimeOffset.UnixEpoch,
                    null));
                Assert.Contains(message, error.Message, StringComparison.Ordinal);
            }

            void ResetAnalysis(Action<JsonObject>? mutate = null)
            {
                analysis = WriteAnalysis(root, library);
                if (mutate is null)
                {
                    return;
                }

                var node = JsonNode.Parse(File.ReadAllText(analysis))!.AsObject();
                mutate(node);
                File.WriteAllText(analysis, node.ToJsonString() + "\n");
            }

            ResetAnalysis(node => node["kind"] = "other");
            Expect("identity is unsupported");
            ResetAnalysis(node => node["sourceBytesModified"] = true);
            Expect("modified source bytes");
            ResetAnalysis(node => node["inputs"]!.AsObject()["extra"] = "nope");
            Expect("does not match current inventory");
            ResetAnalysis(node => node["decoderErrors"]!.AsArray().Add("x"));
            Expect("zero decoder errors");
            File.WriteAllText(analysis, "{\"a\":1,\"a\":2}\n");
            Expect("unreadable");
            File.WriteAllText(analysis, "[]\n");
            Expect("must contain an object");
            MoveAssets(library.Curation, "the_bureau", "station_b", 2);
            Expect("11 through 13");
            MoveAssets(library.Curation, "station_b", "the_bureau", 4);
            Expect("11 through 13");
            MoveAssets(library.Curation, "the_bureau", "station_b", 2);
            Expect("between 30 and 1800", timeout: 1801);
            Expect("between 1 and 4", workers: 0);

            library = WriteLibrary(root);
            ResetAnalysis();
            ReplaceFirstNumber(analysis, "\"sampleRateHz\":44100", "\"sampleRateHz\":1.0");
            Expect("sample rate is not an integer");
            ResetAnalysis();
            ReplaceFirstNumber(analysis, "\"channels\":2", "\"channels\":1.0");
            Expect("channel count is not an integer");

            var versionTimeout = RunReview(root, library, analysis, output, ffmpeg, ffprobe, (_, _, _) =>
                new RadioToolResult(0, string.Empty, string.Empty, true));
            Assert.Equal(2, versionTimeout.ExitCode);
            Assert.Contains("timed out after 30 seconds", versionTimeout.Error, StringComparison.Ordinal);
            var stdoutFailure = RunReview(root, library, analysis, output, ffmpeg, ffprobe, (_, _, _) =>
                new RadioToolResult(4, "from-stdout", string.Empty, false));
            Assert.Contains("exit code 4", stdoutFailure.Error, StringComparison.Ordinal);
            Assert.Contains("from-stdout", stdoutFailure.Error, StringComparison.Ordinal);
            var prefix = new string('P', 40);
            var tail = new string('T', 40);
            var longFailure = RunReview(root, library, analysis, output, ffmpeg, ffprobe, (_, _, _) =>
                new RadioToolResult(4, string.Empty, prefix + new string('n', 1200) + tail, false));
            Assert.Contains(tail, longFailure.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(prefix, longFailure.Error, StringComparison.Ordinal);

            ResetAnalysis();
            var trackTimeout = RunReview(
                root,
                library,
                analysis,
                output,
                ffmpeg,
                ffprobe,
                (executable, arguments, timeout) => arguments.Count == 1 && arguments[0] == "-version"
                    ? new RadioToolResult(0, "ffmpeg version test", string.Empty, false)
                    : new RadioToolResult(0, string.Empty, string.Empty, true),
                timeout: "45");
            Assert.Equal(2, trackTimeout.ExitCode);
            Assert.Contains("review copy failed for", trackTimeout.Error, StringComparison.Ordinal);
            Assert.Contains("timed out after 45 seconds", trackTimeout.Error, StringComparison.Ordinal);
            var missingOutput = RunReview(
                root,
                library,
                analysis,
                output,
                ffmpeg,
                ffprobe,
                new ScriptedTools { WriteOutput = false }.Run);
            Assert.Contains("did not create a regular review copy", missingOutput.Error, StringComparison.Ordinal);
            var parallel = RunReview(
                root,
                library,
                analysis,
                output,
                ffmpeg,
                ffprobe,
                (executable, arguments, timeout) => arguments.Count == 1 && arguments[0] == "-version"
                    ? new RadioToolResult(0, "ffmpeg version test", string.Empty, false)
                    : new RadioToolResult(0, string.Empty, "not-json", false),
                workers: "2");
            Assert.Equal(2, parallel.ExitCode);
            Assert.Contains("review copy failed for", parallel.Error, StringComparison.Ordinal);

            var changed = RunReview(
                root,
                library,
                analysis,
                output,
                ffmpeg,
                ffprobe,
                new ScriptedTools { MutateOrigin = true }.Run);
            Assert.Equal(1, changed.ExitCode);
            Assert.Contains("technical_pass=0 failures=12", changed.Output, StringComparison.Ordinal);
            Assert.Contains(
                "source changed while preparing the review copy",
                File.ReadAllText(Path.Combine(output, "the_bureau", "review-copy-manifest.json")),
                StringComparison.Ordinal);
            Directory.Delete(Path.Combine(output, "the_bureau"), recursive: true);
            library = WriteLibrary(root);
            ResetAnalysis();

            var dynamicTools = new ScriptedTools
            {
                VersionText = string.Empty,
                VersionError = "ffprobe version from-stderr\r\nignored",
                SecondPassType = "dynamic",
            };
            var dynamic = RunReview(root, library, analysis, output, ffmpeg, ffprobe, dynamicTools.Run);
            Assert.Equal(0, dynamic.ExitCode);
            var manifest = File.ReadAllText(Path.Combine(output, "the_bureau", "review-copy-manifest.json"));
            Assert.Contains("\"ffmpeg\": \"ffprobe version from-stderr\"", manifest, StringComparison.Ordinal);
            Assert.Contains("\"dynamicNormalizationCount\": 12", manifest, StringComparison.Ordinal);
            Assert.Contains("\"linearNormalizationCount\": 0", manifest, StringComparison.Ordinal);
            var replaced = RunReview(
                root,
                library,
                analysis,
                output,
                ffmpeg,
                ffprobe,
                dynamicTools.Run,
                replace: "replace");
            Assert.Equal(0, replaced.ExitCode);
            Assert.Empty(Directory.GetDirectories(output, ".the_bureau.backup.*"));
            File.WriteAllText(
                Path.Combine(output, "the_bureau", "review-copy-manifest.json"),
                "{\"kind\":\"vibesnake-radio-review-copy-set-v1\",\"schemaVersion\":1.0,\"stationId\":\"the_bureau\"}\n");
            var fractional = RunReview(
                root,
                library,
                analysis,
                output,
                ffmpeg,
                ffprobe,
                dynamicTools.Run,
                replace: "replace");
            Assert.Equal(2, fractional.ExitCode);
            Assert.Contains("foreign manifest", fractional.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-radio-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string[] ReviewArgs(
        string root,
        LibraryPaths library,
        string analysis,
        string output,
        string ffmpeg,
        string ffprobe,
        string station = "the_bureau",
        string workers = "1",
        string timeout = "30",
        string? replace = null)
    {
        var args = new List<string>
        {
            "radio-review",
            root,
            "prepare",
            station,
            library.Inventory,
            library.Curation,
            analysis,
            output,
            ffmpeg,
            ffprobe,
            workers,
            timeout,
        };
        if (replace is not null)
        {
            args.Add(replace);
        }

        return [.. args];
    }

    private readonly record struct CommandResult(int ExitCode, string Output, string Error);

    private static CommandResult RunReview(
        string root,
        LibraryPaths library,
        string analysis,
        string output,
        string ffmpeg,
        string ffprobe,
        RadioAudioAnalysisCheck.RadioToolRunner runner,
        string workers = "1",
        string timeout = "30",
        string? replace = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = RepositoryCheckCommand.Run(
            ReviewArgs(root, library, analysis, output, ffmpeg, ffprobe, workers: workers, timeout: timeout, replace: replace),
            stdout,
            stderr,
            runner);
        return new CommandResult(exit, stdout.ToString(), stderr.ToString());
    }

    private static void MoveAssets(string curationPath, string fromStation, string toStation, int count)
    {
        var root = JsonNode.Parse(File.ReadAllText(curationPath))!.AsObject();
        JsonArray? from = null;
        JsonArray? to = null;
        foreach (var station in root["stations"]!.AsArray())
        {
            var id = station!["id"]!.GetValue<string>();
            if (id == fromStation)
            {
                from = station["pendingAssetIds"]!.AsArray();
            }
            else if (id == toStation)
            {
                to = station["pendingAssetIds"]!.AsArray();
            }
        }

        for (var index = 0; index < count; index++)
        {
            var assetId = from![0]!.GetValue<string>();
            from.RemoveAt(0);
            to!.Add(assetId);
        }

        File.WriteAllText(curationPath, root.ToJsonString() + "\n");
    }

    private static void ReplaceFirstNumber(string path, string token, string replacement)
    {
        var text = File.ReadAllText(path);
        var index = text.IndexOf(token, StringComparison.Ordinal);
        Assert.True(index >= 0);
        File.WriteAllText(path, string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + token.Length)));
    }

    private static LibraryPaths WriteLibrary(string root, bool collide = false)
    {
        var inventoryPath = Path.Combine(root, "inventory.json");
        var curationPath = Path.Combine(root, "curation.json");
        var assets = new JsonArray
        {
            new JsonObject
            {
                ["id"] = "asset:images/ignored.png",
                ["path"] = "images/ignored.png",
                ["bytes"] = 1,
                ["sha256"] = Policy,
                ["role"] = "runtime-image",
            },
        };
        var bureau = new JsonArray();
        var other = new JsonArray();
        for (var index = 0; index < RadioAudioAnalysisCheck.ExpectedRadioAssets; index++)
        {
            string relative;
            if (collide && index is 0 or 1)
            {
                relative = "audio/radio/" + (index == 0 ? "a" : "b") + "/same.mp3";
            }
            else
            {
                relative = "audio/radio/track-" + index.ToString("000", CultureInfo.InvariantCulture) + ".mp3";
            }

            var bytes = new[] { (byte)(index % 251), (byte)(index / 251), (byte)3 };
            var full = Path.Combine(root, "assets", relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
            var assetId = "asset:" + relative;
            assets.Add(new JsonObject
            {
                ["id"] = assetId,
                ["path"] = relative,
                ["bytes"] = bytes.Length,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                ["role"] = "runtime-radio-track",
            });
            if (index < 12)
            {
                bureau.Add(assetId);
            }
            else
            {
                other.Add(assetId);
            }
        }

        WriteObject(
            inventoryPath,
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["assetRoot"] = "assets",
                ["policySha256"] = Policy,
                ["assets"] = assets,
            });
        WriteObject(
            curationPath,
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["planId"] = "vibesnake-content-curation-v1",
                ["inventoryPolicySha256"] = Policy,
                ["decisionStatus"] = "pending-review",
                ["stations"] = new JsonArray
                {
                    Station("the_bureau", bureau),
                    Station("station_b", other),
                },
            });
        return new LibraryPaths(inventoryPath, curationPath);
    }

    private static JsonObject Station(string id, JsonArray pending) =>
        new()
        {
            ["id"] = id,
            ["pendingAssetIds"] = pending,
            ["approvedAssetIds"] = new JsonArray(),
            ["rejectedAssetIds"] = new JsonArray(),
        };

    private static string WriteAnalysis(string root, LibraryPaths library, bool edgeTrack = false)
    {
        var loaded = RadioAudioAnalysisCheck.LoadRadioAssets(root, library.Inventory, library.Curation);
        var tracks = new JsonArray();
        foreach (var asset in loaded.Assets)
        {
            var duration = 120.0;
            var leading = 0.25;
            var trailing = 0.25;
            if (edgeTrack && asset.RelativePath.EndsWith("track-000.mp3", StringComparison.Ordinal))
            {
                leading = 3.0;
            }
            else if (edgeTrack && asset.RelativePath.EndsWith("track-001.mp3", StringComparison.Ordinal))
            {
                duration = 40.0;
                leading = 8.0;
            }
            else if (edgeTrack && asset.RelativePath.EndsWith("track-002.mp3", StringComparison.Ordinal))
            {
                leading = 3.0;
            }

            tracks.Add(new JsonObject
            {
                ["path"] = asset.RelativePath,
                ["sourceSha256"] = asset.ExpectedSha256,
                ["durationSeconds"] = duration,
                ["leadingSilenceSeconds"] = leading,
                ["trailingSilenceSeconds"] = trailing,
                ["sampleRateHz"] = 44100,
                ["channels"] = 2,
            });
        }

        var analysis = new JsonObject
        {
            ["kind"] = RadioReviewCopyCheck.QualificationKind,
            ["sourceBytesModified"] = false,
            ["inputs"] = new JsonObject
            {
                ["inventorySha256"] = loaded.InventorySha256,
                ["curationSha256"] = loaded.CurationSha256,
                ["inventoryPolicySha256"] = loaded.InventoryPolicySha256,
                ["curationDecisionStatus"] = loaded.CurationDecisionStatus,
            },
            ["tracks"] = tracks,
            ["decoderErrors"] = new JsonArray(),
        };
        var path = Path.Combine(root, "analysis.json");
        WriteObject(path, analysis);
        return path;
    }

    private static void WriteObject(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value.ToJsonString() + "\n");
    }

    private sealed class ScriptedTools
    {
        private readonly object gate = new();
        private readonly Dictionary<string, string> trimByOutput = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> sourceByOutput = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> measures = new(StringComparer.Ordinal);
        private bool mutated;

        internal string VersionText { get; init; } = "ffmpeg version test";

        internal string VersionError { get; init; } = string.Empty;

        internal bool EdgeMode { get; init; }

        internal bool MutateOrigin { get; init; }

        internal bool WriteOutput { get; init; } = true;

        internal string SecondPassType { get; init; } = "linear";

        internal int Progress { get; private set; }

        internal string MeasureFilter { get; private set; } = string.Empty;

        internal string FirstFilter { get; private set; } = string.Empty;

        internal string SecondArguments { get; private set; } = string.Empty;

        internal RadioToolResult Run(string executable, IReadOnlyList<string> arguments, int timeoutSeconds)
        {
            if (arguments.Count == 1 && arguments[0] == "-version")
            {
                return new RadioToolResult(0, VersionText, VersionError, false);
            }

            var source = ArgumentAfter(arguments, "-i");
            if (arguments.Contains("-c:a"))
            {
                var output = arguments[^1];
                lock (gate)
                {
                    SecondArguments = string.Join(", ", arguments.Select(argument => "\"" + argument + "\""));
                    trimByOutput[output] = ArgumentAfter(arguments, "-af");
                    sourceByOutput[output] = source;
                }

                if (WriteOutput)
                {
                    File.WriteAllBytes(output, [1, 2, 3, 4]);
                }

                return new RadioToolResult(0, string.Empty, Loudnorm(SecondPassType), false);
            }

            if (arguments.Any(argument => argument.Contains("silencedetect", StringComparison.Ordinal)))
            {
                var filter = ArgumentAfter(arguments, "-af");
                string trim;
                string origin;
                int measure;
                lock (gate)
                {
                    MeasureFilter = filter;
                    trim = trimByOutput.GetValueOrDefault(source) ?? "atrim=start=0:end=120";
                    origin = sourceByOutput.GetValueOrDefault(source) ?? string.Empty;
                    measures.TryGetValue(origin, out measure);
                    measures[origin] = measure + 1;
                    if (MutateOrigin && origin.Length > 0 && File.Exists(origin))
                    {
                        File.WriteAllBytes(origin, [9, 9, 9]);
                    }

                    if (EdgeMode && !mutated && origin.Length > 0)
                    {
                        var other = Path.Combine(Path.GetDirectoryName(origin)!, "track-090.mp3");
                        if (File.Exists(other))
                        {
                            File.WriteAllBytes(other, [9, 9, 9]);
                            mutated = true;
                        }
                    }
                }

                return new RadioToolResult(0, string.Empty, MeasureLog(origin, trim, measure), false);
            }

            if (arguments.Contains("-of"))
            {
                var output = arguments[^1];
                string trim;
                lock (gate)
                {
                    trim = trimByOutput.GetValueOrDefault(output) ?? "atrim=start=0:end=120";
                }

                return new RadioToolResult(0, Probe(Duration(trim)), string.Empty, false);
            }

            lock (gate)
            {
                FirstFilter = ArgumentAfter(arguments, "-af");
                Progress++;
            }

            return new RadioToolResult(0, string.Empty, Loudnorm("linear"), false);
        }

        private string MeasureLog(string source, string trim, int measure)
        {
            var duration = Duration(trim);
            var leading = 0.25;
            if (EdgeMode && source.Contains("track-000.mp3", StringComparison.Ordinal) && measure == 0)
            {
                leading = 3.0;
            }
            else if (EdgeMode && source.Contains("track-001.mp3", StringComparison.Ordinal))
            {
                leading = 8.0;
            }
            else if (EdgeMode && source.Contains("track-002.mp3", StringComparison.Ordinal))
            {
                leading = 3.0;
            }

            var trailingStart = Math.Max(0.0, duration - 0.25);
            return "Integrated loudness:\n  I: -18.0 LUFS\nLoudness range:\n  LRA: 4.0 LU\nTrue peak:\n  Peak: -1.0 dBFS\n"
                + "n_samples: 10\nmean_volume: -18.0 dB\nmax_volume: -1.0 dB\nhistogram_0db: 0\n"
                + "silence_start: 0\nsilence_end: "
                + leading.ToString(CultureInfo.InvariantCulture)
                + " | silence_duration: "
                + leading.ToString(CultureInfo.InvariantCulture)
                + "\nsilence_start: "
                + trailingStart.ToString(CultureInfo.InvariantCulture)
                + "\nsilence_end: "
                + duration.ToString(CultureInfo.InvariantCulture)
                + " | silence_duration: 0.25\n";
        }

        private static double Duration(string trim)
        {
            var startToken = "atrim=start=";
            var startIndex = trim.IndexOf(startToken, StringComparison.Ordinal);
            var endToken = ":end=";
            var endIndex = trim.IndexOf(endToken, StringComparison.Ordinal);
            if (startIndex < 0 || endIndex < 0)
            {
                return 120.0;
            }

            var start = double.Parse(trim[(startIndex + startToken.Length)..endIndex], CultureInfo.InvariantCulture);
            var endText = trim[(endIndex + endToken.Length)..];
            var comma = endText.IndexOf(',');
            if (comma >= 0)
            {
                endText = endText[..comma];
            }

            var end = double.Parse(endText, CultureInfo.InvariantCulture);
            return Math.Round(end - start, 6, MidpointRounding.ToEven);
        }

        private static string Probe(double duration)
        {
            var text = duration.ToString("0.######", CultureInfo.InvariantCulture);
            return "{\"streams\":[{\"codec_name\":\"flac\",\"sample_rate\":\"44100\",\"channels\":2,\"channel_layout\":\"stereo\",\"duration\":\""
                + text
                + "\",\"bit_rate\":\"8\"}],\"format\":{\"format_name\":\"flac\",\"duration\":\""
                + text
                + "\",\"bit_rate\":\"8\"}}";
        }

        private static string Loudnorm(string type) =>
            """
            {
                "input_i": "-18.00",
                "input_tp": "-1.50",
                "input_lra": "4.00",
                "input_thresh": "-28.00",
                "output_i": "-18.00",
                "output_tp": "-1.00",
                "output_lra": "4.00",
                "output_thresh": "-28.00",
                "normalization_type": "
            """
            + type
            + """
            ",
                "target_offset": "0.00"
            }
            """;

        private static string ArgumentAfter(IReadOnlyList<string> arguments, string name)
        {
            for (var index = 0; index < arguments.Count - 1; index++)
            {
                if (arguments[index] == name)
                {
                    return arguments[index + 1];
                }
            }

            return string.Empty;
        }
    }

    private readonly record struct LibraryPaths(string Inventory, string Curation);
}
