using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public class RadioAudioAnalysisCheckTests
{
    private const string Policy = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string ProbeJson =
        """
        {
          "streams": [
            {
              "codec_name": "mp3",
              "sample_rate": "44100",
              "channels": 2,
              "channel_layout": "stereo",
              "duration": "265.012187",
              "bit_rate": "128000"
            }
          ],
          "format": {
            "format_name": "mp3",
            "duration": "265.012188",
            "bit_rate": "128001"
          }
        }
        """;

    private const string FfmpegLog =
        """
        [Parsed_ebur128_0] Summary:
          Integrated loudness:
            I:         -18.2 LUFS
          Loudness range:
            LRA:         7.4 LU
          True peak:
            Peak:       -1.3 dBFS
        [Parsed_volumedetect_1] n_samples: 23374080
        [Parsed_volumedetect_1] mean_volume: -19.8 dB
        [Parsed_volumedetect_1] max_volume: -1.5 dB
        [Parsed_volumedetect_1] histogram_0db: 0
        [silencedetect] silence_start: 0
        [silencedetect] silence_end: 1.5 | silence_duration: 1.5
        """;

    [Fact]
    public void Ffprobe_parser_requires_one_bounded_audio_stream()
    {
        var probe = RadioAudioAnalysisCheck.ParseFfprobe(ProbeJson);

        Assert.Equal("mp3", probe.Codec);
        Assert.Equal("mp3", probe.Container);
        Assert.Equal(44100, probe.SampleRateHz);
        Assert.Equal(2, probe.Channels);
        Assert.Equal("stereo", probe.ChannelLayout);
        Assert.Equal(265.012188, probe.DurationSeconds, 6);
        Assert.Equal(128001, probe.BitRateBps);
    }

    [Theory]
    [InlineData("""{"streams":[],"format":{}}""")]
    [InlineData("""{"streams":[{"codec_name":"mp3"},{"codec_name":"mp3"}],"format":{}}""")]
    [InlineData("""{"format":{}}""")]
    [InlineData("[]")]
    public void Ffprobe_parser_rejects_missing_or_multiple_audio_streams(string json)
    {
        var error = Assert.Throws<RadioAudioAnalysisException>(() => RadioAudioAnalysisCheck.ParseFfprobe(json));
        Assert.Equal("ffprobe must report exactly one audio stream", error.Message);
    }

    [Fact]
    public void Ffprobe_parser_rejects_incomplete_metadata()
    {
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"44100","channels":2}],"format":"mp3"}""",
            "ffprobe must report container metadata");
        AssertMessage(
            """{"streams":[{"sample_rate":"44100","channels":2,"channel_layout":"stereo"}],"format":{"format_name":"mp3","duration":"40","bit_rate":"1"}}""",
            "ffprobe omitted the audio codec");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"44100","channels":2,"channel_layout":""}],"format":{"format_name":"mp3","duration":"40","bit_rate":"1"}}""",
            "ffprobe emitted an invalid channel layout");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"44100","channels":true,"channel_layout":"mono"}],"format":{"format_name":"mp3","duration":"40","bit_rate":"1"}}""",
            "ffprobe emitted an invalid channel count");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":true,"channels":1}],"format":{"format_name":"mp3","duration":"40","bit_rate":"1"}}""",
            "sample rate is not numeric");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"44100.9","channels":1}],"format":{"duration":"40","bit_rate":"128"}}""",
            "ffprobe omitted the container format");
        AssertMessage("{", "ffprobe did not emit valid JSON");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","codec_name":"mp3","sample_rate":"44100","channels":1}],"format":{"format_name":"mp3","duration":"40","bit_rate":"1"}}""",
            "duplicate JSON field");

        var fallback = RadioAudioAnalysisCheck.ParseFfprobe(
            """
            {"streams":[{"codec_name":"mp3","sample_rate":"48000","channels":1,"duration":"40.5","bit_rate":"64000"}],"format":{"format_name":"mp3"}}
            """);
        Assert.Equal("unknown", fallback.ChannelLayout);
        Assert.Equal(48000, fallback.SampleRateHz);
        Assert.Equal(40.5, fallback.DurationSeconds, 6);
        Assert.Equal(64000, fallback.BitRateBps);
        Assert.Equal(1, fallback.Channels);
    }

    [Fact]
    public void Ffmpeg_parser_uses_final_measurement_summaries()
    {
        var measurement = RadioAudioAnalysisCheck.ParseFfmpeg(
            FfmpegLog
            + "\nmean_volume: -10.0 dB\n"
            + "max_volume: -1.0 dB\n");

        Assert.Equal(-18.2, measurement.IntegratedLufs, 3);
        Assert.Equal(7.4, measurement.LoudnessRangeLu, 3);
        Assert.Equal(-1.3, measurement.TruePeakDbtp, 3);
        Assert.Equal(23374080, measurement.DecodedSampleCount);
        Assert.Equal(-10.0, measurement.MeanVolumeDbfs, 3);
        Assert.Equal(-1.0, measurement.SamplePeakDbfs, 3);
        Assert.Equal(0, measurement.HighestBucketSampleCount);

        var withoutHistogram = RadioAudioAnalysisCheck.ParseFfmpeg(
            """
            Integrated loudness:
              I: -18.0 LUFS
            Loudness range:
              LRA: 1.0 LU
            True peak:
              Peak: -2.0 dBFS
            n_samples: 10
            mean_volume: -18.0 dB
            max_volume: -2.0 dB
            """);
        Assert.Equal(0, withoutHistogram.HighestBucketSampleCount);
    }

    [Fact]
    public void Ffmpeg_parser_rejects_nonfinite_or_incomplete_measurements()
    {
        var nonFinite = Assert.Throws<RadioAudioAnalysisException>(() => RadioAudioAnalysisCheck.ParseFfmpeg(
            """
            Integrated loudness:
              I: -inf LUFS
            Loudness range:
              LRA: 0.0 LU
            True peak:
              Peak: -inf dBFS
            n_samples: 10
            mean_volume: -inf dB
            max_volume: -inf dB
            """));
        Assert.Equal("integrated loudness is not finite", nonFinite.Message);
        AssertMessageLog("I: -18.0 LUFS\nI: -18.0 LUFS\n", "ffmpeg must emit exactly one EBU R 128 summary");
        AssertMessageLog("Integrated loudness:\n  I: -18.0 LUFS\n", "ffmpeg must emit exactly one EBU R 128 summary");
        AssertMessageLog(
            """
            Integrated loudness:
              I: -18.0 LUFS
            Loudness range:
              LRA: 1.0 LU
            True peak:
              Peak: -2.0 dBFS
            mean_volume: -18.0 dB
            max_volume: -2.0 dB
            """,
            "ffmpeg omitted decodedSampleCount");
    }

    [Fact]
    public void Silence_parser_classifies_edges_and_rejects_inconsistent_events()
    {
        var silence = RadioAudioAnalysisCheck.ParseSilence(
            """
            [silencedetect] silence_start: 0
            [silencedetect] silence_end: 1.5 | silence_duration: 1.5
            [silencedetect] silence_start: 20
            [silencedetect] silence_end: 26 | silence_duration: 6
            [silencedetect] silence_start: 98
            [silencedetect] silence_end: 100 | silence_duration: 2
            """,
            100.0);

        Assert.Equal(3, silence.SilenceIntervalCount);
        Assert.Equal(9.5, silence.TotalSilenceSeconds, 6);
        Assert.Equal(1.5, silence.LeadingSilenceSeconds, 6);
        Assert.Equal(2.0, silence.TrailingSilenceSeconds, 6);
        Assert.Equal(6.0, silence.MaximumInternalSilenceSeconds, 6);

        var open = RadioAudioAnalysisCheck.ParseSilence("silence_start: 98", 100.0);
        Assert.Equal(2.0, open.TrailingSilenceSeconds, 6);
        Assert.Equal(0.0, open.LeadingSilenceSeconds, 6);
        Assert.Equal(0.0, open.MaximumInternalSilenceSeconds, 6);

        AssertSilence("silence_end: 2 | silence_duration: 2", 10.0, "end without a start");
        AssertSilence("silence_start: 1\nsilence_start: 2", 10.0, "nested silence intervals");
        AssertSilence("silence_start: 50", 10.0, "outside the track duration");
        AssertSilence("silence_start: 0\nsilence_end: 1.5 | silence_duration: 9", 10.0, "duration disagrees");
        var nonFinite = Assert.Throws<RadioAudioAnalysisException>(() => RadioAudioAnalysisCheck.ParseSilence("", double.NaN));
        Assert.Equal("track duration is not finite", nonFinite.Message);
    }

    [Fact]
    public void Station_summary_retains_failure_breakdown_and_empty_ranges()
    {
        var summary = RadioAudioAnalysisCheck.SummarizeStation(
            "station",
            [
                Row("a", passed: false, integrated: -12.0, peak: 1.0, "integrated loudness is outside the admission band", "true peak exceeds the admission ceiling"),
                Row("b", passed: true, integrated: -18.0, peak: -1.5),
            ],
            1);

        Assert.Equal("station", summary["stationId"]!.GetValue<string>());
        Assert.Equal(3, summary["trackCount"]!.GetValue<int>());
        Assert.Equal(2, summary["measuredTrackCount"]!.GetValue<int>());
        Assert.Equal(1, summary["passedTrackCount"]!.GetValue<int>());
        Assert.Equal(2, summary["failedTrackCount"]!.GetValue<int>());
        Assert.Equal(1, summary["decoderErrorCount"]!.GetValue<int>());
        Assert.Equal(-15.0, summary["averageIntegratedLufs"]!.GetValue<double>(), 3);
        Assert.Equal(-18.0, summary["minimumIntegratedLufs"]!.GetValue<double>(), 3);
        Assert.Equal(-12.0, summary["maximumIntegratedLufs"]!.GetValue<double>(), 3);
        Assert.Equal(1.0, summary["maximumTruePeakDbtp"]!.GetValue<double>(), 3);
        Assert.Equal(1, summary["loudnessFailureCount"]!.GetValue<int>());
        Assert.Equal(1, summary["truePeakFailureCount"]!.GetValue<int>());
        Assert.Equal(0, summary["leadingSilenceFailureCount"]!.GetValue<int>());
        Assert.Equal(0, summary["trailingSilenceFailureCount"]!.GetValue<int>());
        Assert.Equal(0, summary["internalSilenceFailureCount"]!.GetValue<int>());

        var empty = RadioAudioAnalysisCheck.SummarizeStation("quiet", [], 2);
        Assert.Equal(2, empty["trackCount"]!.GetValue<int>());
        using var emptyDocument = JsonDocument.Parse(empty.ToJsonString());
        Assert.Equal(JsonValueKind.Null, emptyDocument.RootElement.GetProperty("averageIntegratedLufs").ValueKind);
        Assert.Equal(JsonValueKind.Null, emptyDocument.RootElement.GetProperty("minimumIntegratedLufs").ValueKind);
        Assert.Equal(JsonValueKind.Null, emptyDocument.RootElement.GetProperty("maximumTruePeakDbtp").ValueKind);
    }

    [Fact]
    public void Final_source_integrity_sweep_covers_measured_and_decoder_error_rows()
    {
        var root = TempDirectory();
        try
        {
            var measured = WriteBytes(root, "audio/measured.mp3", "measured-before"u8.ToArray());
            var failed = WriteBytes(root, "audio/decoder.mp3", "decoder-before"u8.ToArray());
            var assets = new RadioSourceAsset[] { measured, failed };
            var baseline = assets.ToDictionary(
                asset => asset.RelativePath,
                asset => asset.ExpectedSha256,
                StringComparer.Ordinal);
            File.WriteAllBytes(measured.SourcePath, "measured-after"u8.ToArray());
            File.WriteAllBytes(failed.SourcePath, "decoder-after"u8.ToArray());
            var results = new List<TrackMeasurement>
            {
                new()
                {
                    Path = measured.RelativePath,
                    Passed = true,
                    Failures = [],
                },
            };
            var errors = new List<DecoderErrorRow>
            {
                new(failed.RelativePath, "decode failed"),
            };

            var modified = RadioAudioAnalysisCheck.ApplyFinalSourceIntegritySweep(assets, baseline, results, errors);

            Assert.Equal(["audio/decoder.mp3", "audio/measured.mp3"], modified);
            Assert.False(results[0].Passed);
            Assert.Equal(["source changed during analysis"], results[0].Failures);
            Assert.True(errors[0].SourceChangedDuringAnalysis);

            File.WriteAllBytes(measured.SourcePath, "measured-before"u8.ToArray());
            Assert.Single(results[0].Failures);
            var again = RadioAudioAnalysisCheck.ApplyFinalSourceIntegritySweep(
                [measured],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [measured.RelativePath] = Convert.ToHexString(SHA256.HashData("other"u8.ToArray())).ToLowerInvariant(),
                },
                results,
                []);
            Assert.Equal([measured.RelativePath], again);
            Assert.Equal(1, results[0].Failures.Count(failure => failure == "source changed during analysis"));
            var mismatch = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.ApplyFinalSourceIntegritySweep(assets, new Dictionary<string, string>(StringComparer.Ordinal), results, errors));
            Assert.Equal("source-integrity baseline does not match the radio asset set", mismatch.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Measured_track_keeps_admission_failures_and_predicted_gain()
    {
        var root = TempDirectory();
        try
        {
            var asset = WriteBytes(root, "audio/track.mp3", [1, 2, 3]);
            var tools = new ScriptedTools();
            var row = RadioAudioAnalysisCheck.MeasureAsset(asset, "ffmpeg", "ffprobe", 30, tools.Run);

            Assert.True(row.Passed);
            Assert.Empty(row.Failures);
            Assert.Equal("mp3", row.Codec);
            Assert.Equal(1.5, row.LeadingSilenceSeconds, 6);
            Assert.Equal(0.2, row.RecommendedLoudnessGainDb, 3);
            Assert.Equal(-1.1, row.PredictedTruePeakAfterGainDbtp, 3);
            Assert.False(row.LimiterRequiredAtTarget);
            Assert.Equal(
                "ebur128=peak=true:framelog=verbose,volumedetect,silencedetect=noise=-60.0dB:d=1.0",
                tools.Calls.Single(call => call.Arguments.Contains("-af")).Arguments.SkipWhile(item => item != "-af").Skip(1).First());

            var loud = RadioAudioAnalysisCheck.MeasureAsset(
                asset,
                "ffmpeg",
                "ffprobe",
                30,
                new ScriptedTools { FfmpegText = FfmpegLog.Replace("-18.2", "-12.0", StringComparison.Ordinal).Replace("-1.3", "0.4", StringComparison.Ordinal) }.Run);
            Assert.False(loud.Passed);
            Assert.Contains("integrated loudness is outside the admission band", loud.Failures);
            Assert.Contains("true peak exceeds the admission ceiling", loud.Failures);
            Assert.False(loud.LimiterRequiredAtTarget);
            var quiet = RadioAudioAnalysisCheck.MeasureAsset(
                asset,
                "ffmpeg",
                "ffprobe",
                30,
                new ScriptedTools { FfmpegText = FfmpegLog.Replace("-18.2", "-30.0", StringComparison.Ordinal) }.Run);
            Assert.Contains("integrated loudness is outside the admission band", quiet.Failures);
            Assert.DoesNotContain("true peak exceeds the admission ceiling", quiet.Failures);
            Assert.True(quiet.LimiterRequiredAtTarget);

            AssertFailure(asset, ProbeWith(codec: "aac"), FfmpegLog, "codec is not MP3");
            AssertFailure(asset, ProbeWith(channels: 6), FfmpegLog, "channel count is not mono or stereo");
            AssertFailure(asset, ProbeWith(sampleRate: "22050"), FfmpegLog, "sample rate is outside the admitted range");
            AssertFailure(asset, ProbeWith(duration: "10"), FfmpegLog, "duration is outside the admitted range");
            AssertFailure(asset, ProbeWith(duration: "100"), PassingLogWithoutSilence() + SilenceLog(0, 3), "leading silence exceeds the admission ceiling");
            AssertFailure(asset, ProbeWith(duration: "100"), PassingLogWithoutSilence() + SilenceLog(97, 100), "trailing silence exceeds the admission ceiling");
            AssertFailure(asset, ProbeWith(duration: "100"), PassingLogWithoutSilence() + SilenceLog(20, 26), "internal silence exceeds the admission ceiling");
            var edges = RadioAudioAnalysisCheck.MeasureAsset(
                asset,
                "ffmpeg",
                "ffprobe",
                30,
                new ScriptedTools { ProbeText = ProbeWith(duration: "100"), FfmpegText = PassingLogWithoutSilence() + SilenceLog(0, 2) }.Run);
            Assert.True(edges.Passed);
            Assert.Equal(-1.0, RadioAudioAnalysisCheck.ParseFfmpeg(PassingLogWithoutSilence().Replace("-1.3", "-1.0", StringComparison.Ordinal)).TruePeakDbtp, 3);

            var changed = new ScriptedTools
            {
                BeforeDecode = () => File.WriteAllBytes(asset.SourcePath, [9, 9, 9]),
            };
            var mutated = RadioAudioAnalysisCheck.MeasureAsset(asset, "ffmpeg", "ffprobe", 30, changed.Run);
            Assert.Contains("source changed during analysis", mutated.Failures);
            Assert.False(mutated.Passed);

            File.WriteAllBytes(asset.SourcePath, [1, 2, 3, 4]);
            var mismatched = RadioAudioAnalysisCheck.MeasureAsset(
                asset,
                "ffmpeg",
                "ffprobe",
                30,
                new ScriptedTools().Run);
            Assert.Contains("source byte count differs from inventory", mismatched.Failures);
            Assert.Contains("source SHA-256 differs from inventory", mismatched.Failures);

            var probeFailure = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.MeasureAsset(asset, "ffmpeg", "ffprobe", 30, new ScriptedTools { ProbeExitCode = 7, ProbeError = " probe broke " }.Run));
            Assert.Equal("ffprobe failed for audio/track.mp3: probe broke", probeFailure.Message);
            var decodeFailure = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.MeasureAsset(asset, "ffmpeg", "ffprobe", 12, new ScriptedTools { DecodeExitCode = 3, DecodeError = new string('x', 600) }.Run));
            Assert.StartsWith("full decode failed for audio/track.mp3: ", decodeFailure.Message, StringComparison.Ordinal);
            Assert.Equal(500, decodeFailure.Message["full decode failed for audio/track.mp3: ".Length..].Length);
            var timedOut = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.MeasureAsset(asset, "ffmpeg", "ffprobe", 12, new ScriptedTools { Timeout = true }.Run));
            Assert.Contains("timed out after 12 seconds", timedOut.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Inventory_binding_rejects_incomplete_sets_and_unsafe_paths()
    {
        var root = TempDirectory();
        try
        {
            var paths = WriteLibrary(root, 1);
            var count = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.LoadRadioAssets(root, paths.Inventory, paths.Curation));
            Assert.Equal("expected 95 radio assets, found 1", count.Message);

            File.WriteAllText(paths.Inventory, "{}\n");
            AssertLoad(root, paths, "content inventory identity is unsupported");
            WriteObject(paths.Inventory, InventoryObject(root, 1));
            File.WriteAllText(paths.Curation, """{"schemaVersion":1,"planId":"other"}""");
            AssertLoad(root, paths, "content curation identity is unsupported");
            WriteObject(paths.Curation, CurationObject(["asset:audio/track-000.mp3"], policy: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
            AssertLoad(root, paths, "content curation does not match the inventory policy");
            WriteObject(paths.Curation, new JsonObject
            {
                ["schemaVersion"] = 1,
                ["planId"] = "vibesnake-content-curation-v1",
                ["inventoryPolicySha256"] = Policy,
                ["stations"] = new JsonArray(),
            });
            AssertLoad(root, paths, "curation must contain a nonempty stations array");
            WriteObject(paths.Curation, StationProblem(new JsonObject()));
            AssertLoad(root, paths, "curation contains an invalid station");
            WriteObject(paths.Curation, StationProblem(new JsonObject
            {
                ["id"] = "station",
                ["pendingAssetIds"] = new JsonArray(1),
                ["approvedAssetIds"] = new JsonArray(),
                ["rejectedAssetIds"] = new JsonArray(),
            }));
            AssertLoad(root, paths, "curation station station has invalid pendingAssetIds");
            WriteObject(paths.Curation, CurationObject(["asset:audio/track-000.mp3", "asset:audio/track-000.mp3"]));
            AssertLoad(root, paths, "curation assigns an asset more than once: asset:audio/track-000.mp3");

            var inventory = InventoryObject(root, 1);
            inventory["assets"] = new JsonArray(1);
            WriteObject(paths.Inventory, inventory);
            WriteObject(paths.Curation, CurationObject(["asset:audio/track-000.mp3"]));
            AssertLoad(root, paths, "content inventory contains a non-object asset");
            inventory["assets"] = JsonNode.Parse("1");
            WriteObject(paths.Inventory, inventory);
            AssertLoad(root, paths, "content inventory assets must be an array");
            var broken = InventoryObject(root, 1);
            Radio(broken)["sha256"] = "ABC";
            WriteObject(paths.Inventory, broken);
            AssertLoad(root, paths, "content inventory contains an invalid radio asset");
            Radio(broken)["sha256"] = Policy;
            Radio(broken)["bytes"] = JsonNode.Parse("1.0");
            WriteObject(paths.Inventory, broken);
            AssertLoad(root, paths, "content inventory contains an invalid radio asset");
            var repeated = InventoryObject(root, 1);
            repeated["assets"]!.AsArray().Add(Radio(repeated).DeepClone());
            WriteObject(paths.Inventory, repeated);
            AssertLoad(root, paths, "content inventory contains a repeated radio asset");
            var unassigned = InventoryObject(root, 1);
            WriteObject(paths.Inventory, unassigned);
            WriteObject(paths.Curation, CurationObject(["asset:other"]));
            AssertLoad(root, paths, "curation does not assign radio asset: asset:audio/track-000.mp3");
            var extra = InventoryObject(root, 1);
            WriteObject(paths.Inventory, extra);
            WriteObject(paths.Curation, CurationObject(["asset:audio/track-000.mp3", "asset:extra"]));
            AssertLoad(root, paths, "curation and inventory radio sets differ");

            var escaped = InventoryObject(root, 1);
            Radio(escaped)["path"] = "../outside.mp3";
            WriteObject(paths.Inventory, escaped);
            WriteObject(paths.Curation, CurationObject(["asset:audio/track-000.mp3"]));
            AssertLoad(root, paths, "radio asset escapes the asset root: ../outside.mp3");
            File.Delete(Path.Combine(root, "assets", "audio", "track-000.mp3"));
            WriteObject(paths.Inventory, InventoryObject(root, 1, writeFiles: false));
            AssertLoad(root, paths, "radio asset must be a regular file: audio/track-000.mp3");

            Directory.Delete(Path.Combine(root, "assets"), recursive: true);
            WriteObject(paths.Inventory, InventoryObject(root, 1, writeFiles: false));
            AssertLoad(root, paths, "radio asset root must be a regular directory");
            File.WriteAllText(paths.Inventory, "[]");
            AssertLoad(root, paths, "JSON input must contain an object");
            File.WriteAllBytes(paths.Inventory, [0xFF, 0x00]);
            AssertLoad(root, paths, "JSON input is unreadable");
            File.WriteAllText(paths.Inventory, """{"schemaVersion":1,"schemaVersion":1}""");
            AssertLoad(root, paths, "duplicate JSON field");
            File.WriteAllBytes(paths.Inventory, new byte[RadioAudioAnalysisCheck.MaximumJsonBytes + 1]);
            AssertLoad(root, paths, "JSON input exceeds");
            var directoryInput = paths.Curation + "-dir";
            Directory.CreateDirectory(directoryInput);
            var directory = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.LoadRadioAssets(root, directoryInput, paths.Curation));
            Assert.Contains("regular file", directory.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Qualification_measures_the_closed_set_without_approving_release()
    {
        var root = TempDirectory();
        try
        {
            var library = WriteLibrary(root, RadioAudioAnalysisCheck.ExpectedRadioAssets, splitStations: true);
            var tools = new ScriptedTools();
            var progress = new List<string>();
            var measuredAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
            var evidence = RadioAudioAnalysisCheck.Qualify(
                root,
                library.Inventory,
                library.Curation,
                Path.Combine(root, "ffmpeg-stub"),
                Path.Combine(root, "ffprobe-stub"),
                1,
                30,
                tools.Run,
                measuredAt,
                progress.Add);

            Assert.False(evidence["releaseApproved"]!.GetValue<bool>());
            Assert.True(evidence["humanListeningRequired"]!.GetValue<bool>());
            Assert.False(evidence["sourceBytesModified"]!.GetValue<bool>());
            Assert.True(evidence["passed"]!.GetValue<bool>());
            Assert.Equal("vibesnake-radio-audio-qualification-v1", evidence["kind"]!.GetValue<string>());
            Assert.Equal("2026-08-20T12:00:00Z", evidence["measuredUtc"]!.GetValue<string>());
            Assert.Equal("Full decode with FFmpeg EBU R 128 and true-peak measurement", evidence["measurementBasis"]!.GetValue<string>());
            Assert.Equal(-18.0, evidence["admissionPolicy"]!["targetIntegratedLufs"]!.GetValue<double>(), 3);
            Assert.Equal("ffmpeg version test", evidence["toolchain"]!["ffmpeg"]!.GetValue<string>());
            Assert.Equal("ffprobe version test", evidence["toolchain"]!["ffprobe"]!.GetValue<string>());
            Assert.Equal(1, evidence["toolchain"]!["workers"]!.GetValue<int>());
            Assert.Equal(30, evidence["toolchain"]!["perTrackTimeoutSeconds"]!.GetValue<int>());
            Assert.Equal(95, evidence["summary"]!["measuredTrackCount"]!.GetValue<int>());
            Assert.Equal(95, evidence["summary"]!["passedTrackCount"]!.GetValue<int>());
            Assert.Equal(0, evidence["summary"]!["failedTrackCount"]!.GetValue<int>());
            Assert.Equal(0, evidence["summary"]!["decoderErrorCount"]!.GetValue<int>());
            Assert.Equal("pending-review", evidence["inputs"]!["curationDecisionStatus"]!.GetValue<string>());
            Assert.Equal(2, evidence["stations"]!.AsArray().Count);
            Assert.Equal("station_a", evidence["stations"]![0]!["stationId"]!.GetValue<string>());
            Assert.Equal("station_b", evidence["stations"]![1]!["stationId"]!.GetValue<string>());
            Assert.Equal(95, progress.Count);
            Assert.StartsWith("Measured 1/95: audio/track-000.mp3", progress[0], StringComparison.Ordinal);
            Assert.Equal(30, tools.Calls[0].Timeout);
            Assert.Contains(tools.Calls, call => call.Arguments.Contains("-version"));
            Assert.DoesNotContain(tools.Calls.Take(2), call => call.Arguments.Contains("-i"));

            var loudTools = new ScriptedTools
            {
                Next = (executable, arguments) =>
                {
                    if (Mentions(arguments, "track-004.mp3") && arguments.Contains("-af"))
                    {
                        return new RadioToolResult(0, "", FfmpegLog.Replace("-18.2", "-30.0", StringComparison.Ordinal), false);
                    }

                    if (Mentions(arguments, "track-005.mp3") && arguments.Contains("-of"))
                    {
                        return new RadioToolResult(1, "", "decoder broke", false);
                    }

                    return new ScriptedTools().Run(executable, arguments, 30);
                },
            };
            var mixed = new List<string>();
            var mixedGate = new object();
            var failed = RadioAudioAnalysisCheck.Qualify(
                root,
                library.Inventory,
                library.Curation,
                Path.Combine(root, "ffmpeg-stub"),
                Path.Combine(root, "ffprobe-stub"),
                2,
                40,
                loudTools.Run,
                measuredAt,
                line =>
                {
                    lock (mixedGate)
                    {
                        mixed.Add(line);
                    }
                });
            Assert.False(failed["passed"]!.GetValue<bool>());
            Assert.False(failed["releaseApproved"]!.GetValue<bool>());
            Assert.Equal(94, failed["summary"]!["measuredTrackCount"]!.GetValue<int>());
            Assert.Equal(1, failed["summary"]!["decoderErrorCount"]!.GetValue<int>());
            Assert.Equal(2, failed["summary"]!["failedTrackCount"]!.GetValue<int>());
            Assert.Equal(1, failed["summary"]!["loudnessFailureCount"]!.GetValue<int>());
            Assert.Equal(95, mixed.Count);
            var output = Path.Combine(root, "TestResults", "radio-audio", "qualification.json");
            var resolved = RadioAudioAnalysisCheck.RequireOutputPath(root, output, replace: false, library.Inventory, library.Curation);
            RadioAudioAnalysisCheck.WriteQualification(resolved, failed);
            var written = File.ReadAllText(resolved);
            Assert.Contains("\"releaseApproved\": false", written, StringComparison.Ordinal);
            Assert.EndsWith("\n", written, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", written, StringComparison.Ordinal);

            var version = new ScriptedTools { VersionExitCode = 4 };
            var versionError = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.Qualify(root, library.Inventory, library.Curation, "ffmpeg", "ffprobe", 1, 30, version.Run, measuredAt, null));
            Assert.Contains("tool version check failed", versionError.Message, StringComparison.Ordinal);
            var silent = new ScriptedTools { VersionText = "" };
            var silentError = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.Qualify(root, library.Inventory, library.Curation, "ffmpeg", "ffprobe", 1, 30, silent.Run, measuredAt, null));
            Assert.Contains("tool version check returned no output", silentError.Message, StringComparison.Ordinal);
            var workers = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.Qualify(root, library.Inventory, library.Curation, "ffmpeg", "ffprobe", 0, 30, tools.Run, measuredAt, null));
            Assert.Equal("workers must be between 1 and 8", workers.Message);
            var timeout = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.Qualify(root, library.Inventory, library.Curation, "ffmpeg", "ffprobe", 1, 9, tools.Run, measuredAt, null));
            Assert.Equal("timeout-seconds must be between 10 and 600", timeout.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Output_paths_reject_the_repository_inputs_and_overwrite_only_when_asked()
    {
        var root = TempDirectory();
        var outside = TempDirectory();
        try
        {
            var library = WriteLibrary(root, 1);
            var inside = Path.Combine(root, "docs", "qualification.json");
            var rejected = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.RequireOutputPath(root, inside, replace: true));
            Assert.Equal("output inside the repository must stay under TestResults or archive", rejected.Message);
            var archive = RadioAudioAnalysisCheck.RequireOutputPath(root, Path.Combine(root, "archive", "qualification.json"), replace: false);
            Assert.EndsWith(Path.Combine("archive", "qualification.json"), archive, StringComparison.Ordinal);
            var external = RadioAudioAnalysisCheck.RequireOutputPath(root, Path.Combine(outside, "qualification.json"), replace: false);
            Assert.Equal(Path.GetFullPath(Path.Combine(outside, "qualification.json")), external);
            var existing = Path.Combine(root, "TestResults", "already.json");
            Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
            File.WriteAllText(existing, "old\n");
            var exists = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.RequireOutputPath(root, existing, replace: false));
            Assert.Contains("pass --replace to overwrite it", exists.Message, StringComparison.Ordinal);
            Assert.Equal(Path.GetFullPath(existing), RadioAudioAnalysisCheck.RequireOutputPath(root, existing, replace: true));
            var protectedOutput = Path.Combine(root, "TestResults", "protected.json");
            var blocked = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.RequireOutputPath(root, protectedOutput, replace: true, protectedOutput));
            Assert.Equal("radio audio output cannot overwrite an input record", blocked.Message);
            var directory = Path.Combine(root, "TestResults", "qualification.json");
            Directory.CreateDirectory(directory);
            var directoryError = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.RequireOutputPath(root, directory, replace: true));
            Assert.Equal("radio audio output must be a regular file path", directoryError.Message);

            var evidence = new JsonObject { ["releaseApproved"] = false };
            RadioAudioAnalysisCheck.WriteQualification(external, evidence);
            Assert.Contains("\"releaseApproved\": false", File.ReadAllText(external), StringComparison.Ordinal);
            var fractional = RadioAudioAnalysisCheck.FormatUtc(new DateTimeOffset(2026, 8, 20, 12, 0, 0, 123, TimeSpan.Zero).AddTicks(4560));
            Assert.EndsWith("Z", fractional, StringComparison.Ordinal);
            Assert.Contains(".", fractional, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Command_qualifies_captured_logs_and_rejects_bad_invocation()
    {
        var root = TempDirectory();
        var outside = TempDirectory();
        try
        {
            var library = WriteLibrary(root, RadioAudioAnalysisCheck.ExpectedRadioAssets);
            var ffmpeg = Path.Combine(root, "ffmpeg-stub");
            var ffprobe = Path.Combine(root, "ffprobe-stub");
            File.WriteAllBytes(ffmpeg, []);
            File.WriteAllBytes(ffprobe, []);
            var output = Path.Combine(root, "TestResults", "radio-audio", "qualification.json");
            var tools = new ScriptedTools();
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exit = RepositoryCheckCommand.Run(
                QualifyArgs(root, library.Inventory, library.Curation, output, ffmpeg, ffprobe),
                stdout,
                stderr,
                tools.Run);

            Assert.Equal(0, exit);
            Assert.Contains("tracks=95/95 passed=95 failed=0", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Measured 95/95:", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("\"releaseApproved\": false", File.ReadAllText(output), StringComparison.Ordinal);

            var loud = new ScriptedTools
            {
                FfmpegText = FfmpegLog.Replace("-18.2", "-30.0", StringComparison.Ordinal),
            };
            var failedOut = new StringWriter();
            var failedErr = new StringWriter();
            var failed = RepositoryCheckCommand.Run(
                QualifyArgs(root, library.Inventory, library.Curation, output, ffmpeg, ffprobe, replace: "replace"),
                failedOut,
                failedErr,
                loud.Run);
            Assert.Equal(1, failed);
            Assert.Contains("passed=0 failed=95", failedOut.ToString(), StringComparison.Ordinal);
            Assert.Contains("\"releaseApproved\": false", File.ReadAllText(output), StringComparison.Ordinal);

            var usage = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(["radio-audio"], new StringWriter(), usage));
            Assert.Contains("radio-audio <repository-root> qualify", usage.ToString(), StringComparison.Ordinal);
            var badRoot = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(QualifyArgs("bad\0root", library.Inventory, library.Curation, output, ffmpeg, ffprobe), new StringWriter(), badRoot, tools.Run));
            Assert.Contains("Repository root is invalid.", badRoot.ToString(), StringComparison.Ordinal);
            var badPath = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(QualifyArgs(root, " ", library.Curation, output, ffmpeg, ffprobe), new StringWriter(), badPath, tools.Run));
            Assert.Contains("Radio audio path is invalid.", badPath.ToString(), StringComparison.Ordinal);
            var badWorkers = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(QualifyArgs(root, library.Inventory, library.Curation, output, ffmpeg, ffprobe, workers: "08"), new StringWriter(), badWorkers, tools.Run));
            Assert.Contains("qualify <inventory>", badWorkers.ToString(), StringComparison.Ordinal);
            var range = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(QualifyArgs(root, library.Inventory, library.Curation, output, ffmpeg, ffprobe, workers: "9"), new StringWriter(), range, tools.Run));
            Assert.Contains("workers must be between 1 and 8", range.ToString(), StringComparison.Ordinal);
            var timeout = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(QualifyArgs(root, library.Inventory, library.Curation, output, ffmpeg, ffprobe, timeout: "601"), new StringWriter(), timeout, tools.Run));
            Assert.Contains("timeout-seconds must be between 10 and 600", timeout.ToString(), StringComparison.Ordinal);
            var missing = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(QualifyArgs(root, library.Inventory, library.Curation, output, "missing-ffmpeg-tool", ffprobe), new StringWriter(), missing, tools.Run));
            Assert.Contains("ffmpeg and ffprobe must both be available", missing.ToString(), StringComparison.Ordinal);
            var docs = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(QualifyArgs(root, library.Inventory, library.Curation, Path.Combine(root, "docs", "out.json"), ffmpeg, ffprobe), new StringWriter(), docs, tools.Run));
            Assert.Contains("TestResults or archive", docs.ToString(), StringComparison.Ordinal);
            var external = Path.Combine(outside, "qualification.json");
            var externalExit = RepositoryCheckCommand.Run(
                QualifyArgs(root, library.Inventory, library.Curation, external, ffmpeg, ffprobe),
                new StringWriter(),
                new StringWriter(),
                tools.Run);
            Assert.Equal(0, externalExit);
            Assert.True(File.Exists(external));

            var natural = new StringWriter();
            var naturalExit = RepositoryCheckCommand.Run(
                QualifyArgs(root, library.Inventory, library.Curation, Path.Combine(outside, "natural.json"), ffmpeg, ffprobe),
                new StringWriter(),
                natural);
            Assert.Equal(2, naturalExit);
            Assert.Contains("Radio audio analysis failed:", natural.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Tool_resolution_and_production_runner_fail_closed()
    {
        Assert.Null(RadioAudioAnalysisCheck.FindTool(" "));
        Assert.Null(RadioAudioAnalysisCheck.FindTool("a\0b"));
        Assert.Null(RadioAudioAnalysisCheck.FindTool("definitely-missing-radio-audio-tool"));
        var rooted = Path.Combine(Path.GetTempPath(), "missing-radio-audio-" + Guid.NewGuid().ToString("N"));
        Assert.Null(RadioAudioAnalysisCheck.FindTool(rooted));
        if (OperatingSystem.IsWindows())
        {
            var command = RadioAudioAnalysisCheck.FindTool("cmd");
            Assert.False(string.IsNullOrWhiteSpace(command));
            Assert.True(File.Exists(command));
        }

        var toolDirectory = Path.Combine(Path.GetTempPath(), "vibesnake-radio-audio-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(toolDirectory);
        try
        {
            var toolName = "radio-audio-tool";
            var toolFile = Path.Combine(toolDirectory, OperatingSystem.IsWindows() ? toolName + ".exe" : toolName);
            File.WriteAllBytes(toolFile, [0x7F]);
            var found = RadioAudioAnalysisCheck.FindTool(toolName, toolDirectory);
            Assert.Equal(Path.GetFullPath(toolFile), found);
            Assert.True(File.Exists(found));
            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(toolDirectory, "linked-tool");
                try
                {
                    File.CreateSymbolicLink(link, toolFile);
                    Assert.Null(RadioAudioAnalysisCheck.FindTool("linked-tool", toolDirectory));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        finally
        {
            Directory.Delete(toolDirectory, recursive: true);
        }

        var executable = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : "/bin/sh";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "/d", "/c", "echo radio-audio-ok" }
            : new[] { "-c", "echo radio-audio-ok" };
        var result = RadioAudioAnalysisCheck.RunProductionTool(executable, arguments, 30, Path.GetTempPath());
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("radio-audio-ok", result.StandardOutput, StringComparison.Ordinal);

        var missing = Assert.Throws<RadioAudioAnalysisException>(() =>
            RadioAudioAnalysisCheck.RunProductionTool(rooted, [], 10, Path.GetTempPath()));
        Assert.Contains("tool execution failed", missing.Message, StringComparison.Ordinal);
    }

    private static void AssertMessage(string json, string expected) =>
        Assert.Contains(expected, Assert.Throws<RadioAudioAnalysisException>(() => RadioAudioAnalysisCheck.ParseFfprobe(json)).Message, StringComparison.Ordinal);

    private static void AssertMessageLog(string log, string expected) =>
        Assert.Contains(expected, Assert.Throws<RadioAudioAnalysisException>(() => RadioAudioAnalysisCheck.ParseFfmpeg(log)).Message, StringComparison.Ordinal);

    private static void AssertSilence(string log, double duration, string expected) =>
        Assert.Contains(expected, Assert.Throws<RadioAudioAnalysisException>(() => RadioAudioAnalysisCheck.ParseSilence(log, duration)).Message, StringComparison.Ordinal);

    private static void AssertLoad(string root, LibraryPaths paths, string expected) =>
        Assert.Contains(
            expected,
            Assert.Throws<RadioAudioAnalysisException>(() => RadioAudioAnalysisCheck.LoadRadioAssets(root, paths.Inventory, paths.Curation)).Message,
            StringComparison.Ordinal);

    private static void AssertFailure(RadioSourceAsset asset, string probe, string ffmpeg, string expected)
    {
        var row = RadioAudioAnalysisCheck.MeasureAsset(
            asset,
            "ffmpeg",
            "ffprobe",
            30,
            new ScriptedTools { ProbeText = probe, FfmpegText = ffmpeg }.Run);
        Assert.Contains(expected, row.Failures);
    }

    [Fact]
    public void Closed_edges_reject_unsafe_identity_paths_and_unbounded_measurements()
    {
        AssertMessageLog(
            PassingLogWithoutSilence().Replace("n_samples: 10", "n_samples: " + new string('9', 40), StringComparison.Ordinal),
            "decodedSampleCount is not numeric");
        AssertMessageLog(
            PassingLogWithoutSilence().Replace(
                "histogram_0db: 0",
                "histogram_0db: " + new string('9', 40),
                StringComparison.Ordinal),
            "highestBucketSampleCount is not numeric");
        AssertMessageLog(
            PassingLogWithoutSilence().Replace("mean_volume: -19.8 dB\n", string.Empty, StringComparison.Ordinal),
            "ffmpeg omitted meanVolumeDbfs");
        AssertMessageLog(
            PassingLogWithoutSilence().Replace("LRA: 7.4 LU", "LRA: inf LU", StringComparison.Ordinal),
            "loudness range is not finite");
        var openDuration = RadioAudioAnalysisCheck.ParseSilence("silence_start: 0\nsilence_end: 1.5", 10.0);
        Assert.Equal(1.5, openDuration.LeadingSilenceSeconds, 6);
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"44100","channels":2,"duration":"40","bit_rate":"8"}],"format":{"format_name":"mp3","duration":null,"bit_rate":"8"}}""",
            "duration is not numeric");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"44100","channels":2,"duration":"40","bit_rate":"8"}],"format":{"format_name":"mp3","duration":"40","bit_rate":null}}""",
            "bit rate is not numeric");
        AssertMessage(
            """{"streams":[{"codec_name":"","sample_rate":"44100","channels":2,"channel_layout":"stereo"}],"format":{"format_name":"mp3","duration":"40","bit_rate":"8"}}""",
            "ffprobe omitted the audio codec");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"44100","channels":0,"channel_layout":"stereo"}],"format":{"format_name":"mp3","duration":"40","bit_rate":"8"}}""",
            "ffprobe emitted an invalid channel count");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"44100","channels":1.0,"channel_layout":"mono"}],"format":{"format_name":"mp3","duration":"40","bit_rate":"8"}}""",
            "ffprobe emitted an invalid channel count");
        AssertMessage(
            """{"streams":[{"codec_name":"mp3","sample_rate":"1e20","channels":1,"channel_layout":"mono"}],"format":{"format_name":"mp3","duration":"40","bit_rate":"8"}}""",
            "sample rate is not numeric");
        Assert.Null(RadioAudioAnalysisCheck.FindTool(Path.GetTempPath()));
        Assert.Null(RadioAudioAnalysisCheck.FindTool("missing/tool"));

        var root = TempDirectory();
        string? junction = null;
        try
        {
            var paths = WriteLibrary(root, 1);
            var assigned = CurationObject(["asset:audio/track-000.mp3"]);

            void Reject(JsonObject inventory, JsonObject curation, string expected)
            {
                WriteObject(paths.Inventory, inventory);
                WriteObject(paths.Curation, curation);
                AssertLoad(root, paths, expected);
            }

            var windowsPath = InventoryObject(root, 1);
            Radio(windowsPath)["path"] = "audio\\track.mp3";
            Reject(windowsPath, assigned, "radio asset escapes the asset root");
            var rooted = InventoryObject(root, 1);
            Radio(rooted)["path"] = "/outside.mp3";
            Reject(rooted, assigned, "radio asset escapes the asset root");
            var dotted = InventoryObject(root, 1);
            Radio(dotted)["path"] = "audio/./track.mp3";
            Reject(dotted, assigned, "radio asset escapes the asset root");
            var emptySegment = InventoryObject(root, 1);
            Radio(emptySegment)["path"] = "audio//track.mp3";
            Reject(emptySegment, assigned, "radio asset escapes the asset root");
            var control = InventoryObject(root, 1);
            Radio(control)["path"] = "audio/\u0001track.mp3";
            Reject(control, assigned, "radio asset escapes the asset root");
            var fractionalIdentity = InventoryObject(root, 1);
            fractionalIdentity["schemaVersion"] = JsonNode.Parse("1.0");
            Reject(fractionalIdentity, assigned, "content inventory identity is unsupported");
            var renamedRoot = InventoryObject(root, 1);
            renamedRoot["assetRoot"] = "media";
            Reject(renamedRoot, assigned, "content inventory identity is unsupported");
            var fractionalCuration = CurationObject(["asset:audio/track-000.mp3"]);
            fractionalCuration["schemaVersion"] = JsonNode.Parse("1.0");
            Reject(InventoryObject(root, 1), fractionalCuration, "content curation identity is unsupported");
            var renamedPlan = CurationObject(["asset:audio/track-000.mp3"]);
            renamedPlan["planId"] = "other-plan";
            Reject(InventoryObject(root, 1), renamedPlan, "content curation identity is unsupported");
            var booleanBytes = InventoryObject(root, 1);
            Radio(booleanBytes)["bytes"] = JsonNode.Parse("true");
            Reject(booleanBytes, assigned, "content inventory contains an invalid radio asset");
            var duplicatePath = InventoryObject(root, 1);
            var clone = Radio(duplicatePath).DeepClone().AsObject();
            clone["id"] = "asset:audio/other.mp3";
            duplicatePath["assets"]!.AsArray().Add(clone);
            Reject(
                duplicatePath,
                CurationObject(["asset:audio/track-000.mp3", "asset:audio/other.mp3"]),
                "content inventory contains a repeated radio asset");
            var pendingText = CurationObject(["asset:audio/track-000.mp3"]);
            pendingText["stations"]![0]!.AsObject()["pendingAssetIds"] = JsonNode.Parse("\"asset:audio/track-000.mp3\"");
            Reject(InventoryObject(root, 1), pendingText, "curation station station has invalid pendingAssetIds");
            var approvedNumber = CurationObject(["asset:audio/track-000.mp3"]);
            approvedNumber["stations"]![0]!.AsObject()["approvedAssetIds"] = new JsonArray(1);
            Reject(InventoryObject(root, 1), approvedNumber, "curation station station has invalid approvedAssetIds");
            var rejectedNumber = CurationObject(["asset:audio/track-000.mp3"]);
            rejectedNumber["stations"]![0]!.AsObject()["rejectedAssetIds"] = JsonNode.Parse("1");
            Reject(InventoryObject(root, 1), rejectedNumber, "curation station station has invalid rejectedAssetIds");

            var asset = WriteBytes(root, "audio/track.mp3", [1, 2, 3]);
            var ceiling = RadioAudioAnalysisCheck.MeasureAsset(
                asset,
                "ffmpeg",
                "ffprobe",
                30,
                new ScriptedTools
                {
                    ProbeText = ProbeWith(sampleRate: "192000", channels: 1, duration: "900"),
                    FfmpegText = PassingLogWithoutSilence(),
                }.Run);
            Assert.True(ceiling.Passed);
            Assert.Equal(1, ceiling.Channels);
            Assert.Equal(192000, ceiling.SampleRateHz);
            Assert.Equal(900, ceiling.DurationSeconds, 3);
            var minimum = RadioAudioAnalysisCheck.MeasureAsset(
                asset,
                "ffmpeg",
                "ffprobe",
                30,
                new ScriptedTools { ProbeText = ProbeWith(duration: "30"), FfmpegText = PassingLogWithoutSilence() }.Run);
            Assert.True(minimum.Passed);
            AssertFailure(asset, ProbeWith(sampleRate: "192001"), PassingLogWithoutSilence(), "sample rate is outside the admitted range");
            AssertFailure(asset, ProbeWith(duration: "901"), PassingLogWithoutSilence(), "duration is outside the admitted range");
            var bothEdges = RadioAudioAnalysisCheck.MeasureAsset(
                asset,
                "ffmpeg",
                "ffprobe",
                30,
                new ScriptedTools
                {
                    ProbeText = ProbeWith(duration: "100"),
                    FfmpegText = PassingLogWithoutSilence() + SilenceLog(0, 100),
                }.Run);
            Assert.Contains("leading silence exceeds the admission ceiling", bothEdges.Failures);
            Assert.Contains("trailing silence exceeds the admission ceiling", bothEdges.Failures);
            Assert.DoesNotContain("internal silence exceeds the admission ceiling", bothEdges.Failures);
            if (OperatingSystem.IsWindows())
            {
                using var locked = new FileStream(asset.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None);
                var blocked = Assert.Throws<RadioAudioAnalysisException>(() =>
                    RadioAudioAnalysisCheck.MeasureAsset(asset, "ffmpeg", "ffprobe", 30, new ScriptedTools().Run));
                Assert.Contains("source read failed", blocked.Message, StringComparison.Ordinal);
                Assert.True(locked.Length > 0);
            }

            var extra = Assert.Throws<RadioAudioAnalysisException>(() =>
                RadioAudioAnalysisCheck.RequireOutputPath(root, Path.Combine(root, "TestResultsExtra", "out.json"), replace: false));
            Assert.Equal("output inside the repository must stay under TestResults or archive", extra.Message);
            if (OperatingSystem.IsWindows())
            {
                var allowed = RadioAudioAnalysisCheck.RequireOutputPath(
                    root,
                    Path.Combine(root, "testresults", "nested", "out.json"),
                    replace: false);
                Assert.EndsWith(Path.Combine("testresults", "nested", "out.json"), allowed, StringComparison.OrdinalIgnoreCase);
                var real = Path.Combine(root, "TestResults", "real");
                junction = Path.Combine(root, "TestResults", "link");
                Directory.CreateDirectory(real);
                var linker = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/d /c mklink /J \"" + junction + "\" \"" + real + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                Assert.NotNull(linker);
                Assert.True(linker.WaitForExit(10000));
                Assert.Equal(0, linker.ExitCode);
                var throughJunction = Assert.Throws<RadioAudioAnalysisException>(() =>
                    RadioAudioAnalysisCheck.RequireOutputPath(root, Path.Combine(junction, "out.json"), replace: false));
                Assert.Equal("radio audio output must be a regular file path", throughJunction.Message);
            }

            TryRejectLinkedAsset(root, paths);
            var full = WriteLibrary(root, RadioAudioAnalysisCheck.ExpectedRadioAssets);
            var ids = Enumerable.Range(0, RadioAudioAnalysisCheck.ExpectedRadioAssets)
                .Select(index => "asset:audio/track-" + index.ToString("000", CultureInfo.InvariantCulture) + ".mp3")
                .ToArray();
            var curation = CurationObject(ids);
            curation.Remove("decisionStatus");
            WriteObject(full.Curation, curation);
            Assert.Equal("None", RadioAudioAnalysisCheck.LoadRadioAssets(root, full.Inventory, full.Curation).CurationDecisionStatus);
            curation["decisionStatus"] = JsonNode.Parse("true");
            WriteObject(full.Curation, curation);
            Assert.Equal("True", RadioAudioAnalysisCheck.LoadRadioAssets(root, full.Inventory, full.Curation).CurationDecisionStatus);
            curation["decisionStatus"] = JsonNode.Parse("false");
            WriteObject(full.Curation, curation);
            Assert.Equal("False", RadioAudioAnalysisCheck.LoadRadioAssets(root, full.Inventory, full.Curation).CurationDecisionStatus);
            curation["decisionStatus"] = JsonNode.Parse("2");
            WriteObject(full.Curation, curation);
            Assert.Equal("2", RadioAudioAnalysisCheck.LoadRadioAssets(root, full.Inventory, full.Curation).CurationDecisionStatus);
            curation["decisionStatus"] = new JsonObject { ["state"] = "later" };
            WriteObject(full.Curation, curation);
            Assert.Contains(
                "later",
                RadioAudioAnalysisCheck.LoadRadioAssets(root, full.Inventory, full.Curation).CurationDecisionStatus,
                StringComparison.Ordinal);
            curation["decisionStatus"] = "pending-review";
            WriteObject(full.Curation, curation);
            var stderrVersion = new ScriptedTools
            {
                VersionText = string.Empty,
                VersionError = "  version from stderr  \r\nrest",
            };
            var evidence = RadioAudioAnalysisCheck.Qualify(
                root,
                full.Inventory,
                full.Curation,
                "ffmpeg",
                "ffprobe",
                1,
                10,
                stderrVersion.Run,
                new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
                null);
            Assert.Equal("version from stderr", evidence["toolchain"]!["ffmpeg"]!.GetValue<string>());
            Assert.Equal("version from stderr", evidence["toolchain"]!["ffprobe"]!.GetValue<string>());
            Assert.Equal(10, evidence["toolchain"]!["perTrackTimeoutSeconds"]!.GetValue<int>());
        }
        finally
        {
            if (junction is not null && Directory.Exists(junction))
            {
                Directory.Delete(junction, recursive: false);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static void TryRejectLinkedAsset(string root, LibraryPaths paths)
    {
        var target = Path.Combine(root, "assets", "audio", "track-000.mp3");
        var link = Path.Combine(root, "assets", "audio", "linked.mp3");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var inventory = InventoryObject(root, 1, writeFiles: false);
        Radio(inventory)["path"] = "audio/linked.mp3";
        WriteObject(paths.Inventory, inventory);
        WriteObject(paths.Curation, CurationObject(["asset:audio/track-000.mp3"]));
        AssertLoad(root, paths, "radio asset must be a regular file: audio/linked.mp3");
        File.Delete(link);
    }

    private static bool Mentions(IReadOnlyList<string> arguments, string fragment) =>
        arguments.Any(argument => argument.Contains(fragment, StringComparison.Ordinal));

    private static JsonObject Radio(JsonObject inventory) =>
        inventory["assets"]!
            .AsArray()
            .Select(node => node!.AsObject())
            .Single(node => node["role"]!.GetValue<string>() == "runtime-radio-track");

    private static TrackMeasurement Row(string path, bool passed, double integrated, double peak, params string[] failures) =>
        new()
        {
            Path = path,
            Passed = passed,
            IntegratedLufs = integrated,
            TruePeakDbtp = peak,
            Failures = [.. failures],
        };

    private static string ProbeWith(string codec = "mp3", string sampleRate = "44100", int channels = 2, string duration = "265.012188") =>
        "{\"streams\":[{\"codec_name\":\""
        + codec
        + "\",\"sample_rate\":\""
        + sampleRate
        + "\",\"channels\":"
        + channels.ToString(CultureInfo.InvariantCulture)
        + ",\"channel_layout\":\"stereo\",\"duration\":\""
        + duration
        + "\",\"bit_rate\":\"128000\"}],\"format\":{\"format_name\":\"mp3\",\"duration\":\""
        + duration
        + "\",\"bit_rate\":\"128001\"}}";

    private static string SilenceLog(double start, double end) =>
        "silence_start: "
        + start.ToString(CultureInfo.InvariantCulture)
        + "\nsilence_end: "
        + end.ToString(CultureInfo.InvariantCulture)
        + " | silence_duration: "
        + (end - start).ToString(CultureInfo.InvariantCulture)
        + "\n";

    private static string PassingLogWithoutSilence() =>
        """
        Integrated loudness:
          I: -18.2 LUFS
        Loudness range:
          LRA: 7.4 LU
        True peak:
          Peak: -1.3 dBFS
        n_samples: 10
        mean_volume: -19.8 dB
        max_volume: -1.5 dB
        histogram_0db: 0
        """;

    private static RadioSourceAsset WriteBytes(string root, string relative, byte[] bytes)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return new RadioSourceAsset(
            "asset:" + relative,
            "station",
            relative,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            full);
    }

    private static LibraryPaths WriteLibrary(string root, int count, bool splitStations = false, bool writeFiles = true)
    {
        var inventory = Path.Combine(root, "inventory.json");
        var curation = Path.Combine(root, "curation.json");
        WriteObject(inventory, InventoryObject(root, count, writeFiles));
        var ids = Enumerable.Range(0, count)
            .Select(index => "asset:audio/track-" + index.ToString("000", CultureInfo.InvariantCulture) + ".mp3")
            .ToArray();
        if (!splitStations)
        {
            WriteObject(curation, CurationObject(ids));
        }
        else
        {
            WriteObject(curation, new JsonObject
            {
                ["schemaVersion"] = 1,
                ["planId"] = "vibesnake-content-curation-v1",
                ["inventoryPolicySha256"] = Policy,
                ["decisionStatus"] = "pending-review",
                ["stations"] = new JsonArray
                {
                    Station("station_a", ids.Skip(1).ToArray()),
                    Station("station_b", [ids[0]]),
                },
            });
        }

        return new LibraryPaths(inventory, curation);
    }

    private static JsonObject InventoryObject(string root, int count, bool writeFiles = true)
    {
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
        for (var index = 0; index < count; index++)
        {
            var relative = "audio/track-" + index.ToString("000", CultureInfo.InvariantCulture) + ".mp3";
            var bytes = new[] { (byte)(index % 251), (byte)(index / 251) };
            if (writeFiles)
            {
                var full = Path.Combine(root, "assets", relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, bytes);
            }

            assets.Add(new JsonObject
            {
                ["id"] = "asset:" + relative,
                ["path"] = relative,
                ["bytes"] = bytes.Length,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                ["role"] = "runtime-radio-track",
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["assetRoot"] = "assets",
            ["policySha256"] = Policy,
            ["assets"] = assets,
        };
    }

    private static JsonObject CurationObject(IReadOnlyList<string> ids, string? policy = null) =>
        new()
        {
            ["schemaVersion"] = 1,
            ["planId"] = "vibesnake-content-curation-v1",
            ["inventoryPolicySha256"] = policy ?? Policy,
            ["decisionStatus"] = "pending-review",
            ["stations"] = new JsonArray { Station("station", ids) },
        };

    private static JsonObject Station(string id, IReadOnlyList<string> pending)
    {
        var ids = new JsonArray();
        foreach (var assetId in pending)
        {
            ids.Add(assetId);
        }

        return new JsonObject
        {
            ["id"] = id,
            ["pendingAssetIds"] = ids,
            ["approvedAssetIds"] = new JsonArray(),
            ["rejectedAssetIds"] = new JsonArray(),
        };
    }

    private static JsonObject StationProblem(JsonObject station) =>
        new()
        {
            ["schemaVersion"] = 1,
            ["planId"] = "vibesnake-content-curation-v1",
            ["inventoryPolicySha256"] = Policy,
            ["stations"] = new JsonArray(station),
        };

    private static void WriteObject(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value.ToJsonString() + "\n");
    }

    private static string[] QualifyArgs(
        string root,
        string inventory,
        string curation,
        string output,
        string ffmpeg,
        string ffprobe,
        string workers = "1",
        string timeout = "30",
        string? replace = null)
    {
        var args = new List<string>
        {
            "radio-audio",
            root,
            "qualify",
            inventory,
            curation,
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

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-radio-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private readonly record struct LibraryPaths(string Inventory, string Curation);

    private sealed class ScriptedTools
    {
        internal List<(string Executable, IReadOnlyList<string> Arguments, int Timeout)> Calls { get; } = [];

        internal string ProbeText { get; init; } = ProbeJson;

        internal string FfmpegText { get; init; } = FfmpegLog;

        internal int ProbeExitCode { get; init; }

        internal int DecodeExitCode { get; init; }

        internal int VersionExitCode { get; init; }

        internal string ProbeError { get; init; } = string.Empty;

        internal string DecodeError { get; init; } = string.Empty;

        internal string? VersionText { get; init; }

        internal string VersionError { get; init; } = string.Empty;

        internal bool Timeout { get; init; }

        internal Action? BeforeDecode { get; init; }

        internal Func<string, IReadOnlyList<string>, RadioToolResult>? Next { get; init; }

        internal RadioToolResult Run(string executable, IReadOnlyList<string> arguments, int timeout)
        {
            lock (Calls)
            {
                Calls.Add((executable, arguments.ToArray(), timeout));
            }

            if (Timeout)
            {
                return new RadioToolResult(-1, string.Empty, string.Empty, true);
            }

            if (arguments.Count == 1 && arguments[0] == "-version")
            {
                if (VersionText is not null)
                {
                    return new RadioToolResult(VersionExitCode, VersionText, VersionError, false);
                }

                var text = executable.Contains("ffprobe", StringComparison.OrdinalIgnoreCase)
                    ? "ffprobe version test\n"
                    : "ffmpeg version test\n";
                return new RadioToolResult(VersionExitCode, text, string.Empty, false);
            }

            if (Next is not null)
            {
                return Next(executable, arguments);
            }

            if (arguments.Contains("-of"))
            {
                return new RadioToolResult(ProbeExitCode, ProbeText, ProbeError, false);
            }

            BeforeDecode?.Invoke();
            return new RadioToolResult(DecodeExitCode, string.Empty, DecodeError.Length == 0 ? FfmpegText : DecodeError, false);
        }
    }
}
