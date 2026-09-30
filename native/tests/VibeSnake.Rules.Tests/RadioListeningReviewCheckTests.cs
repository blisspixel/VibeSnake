using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class RadioListeningReviewCheckTests
{
    private static readonly string[] Criteria =
    [
        "complete-playback",
        "no-audible-clipping-or-distortion",
        "clean-start-and-end",
        "relative-level-consistency",
        "station-identity-fit",
        "sustained-listening-comfort",
    ];

    private static readonly string[] ConfirmationFields =
    [
        "listenedToEveryTrackInFull",
        "comparedRelativeLevels",
        "reviewedEveryTrackOnAllDeclaredDevices",
        "understandsNoSourceOrReleaseStateChangesAutomatically",
    ];

    private static readonly JsonSerializerOptions InputJson = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    [Fact]
    public void Template_binds_every_copy_and_stays_pending()
    {
        var root = TempDirectory();
        var review = Path.Combine(TempDirectory(), "the_bureau");
        try
        {
            WriteReviewSet(review, reverseOrder: true);
            var output = Path.Combine(review, "listening-review.json.template");
            var preparation = RadioListeningReviewCheck.PrepareTemplate(root, review, output);
            var bytes = File.ReadAllBytes(output);
            AssertCanonical(bytes);
            var template = JsonNode.Parse(bytes)!.AsObject();

            Assert.Equal("the_bureau", preparation.StationId);
            Assert.Equal(2, preparation.TrackCount);
            Assert.Equal(output, preparation.OutputPath);
            Assert.Equal("the_bureau", template["stationId"]!.GetValue<string>());
            Assert.Equal("radio-reviewer-REPLACE", template["reviewerId"]!.GetValue<string>());
            Assert.Equal("REPLACE", template["executedUtc"]!.GetValue<string>());
            var reviews = template["trackReviews"]!.AsArray();
            Assert.Equal(2, reviews.Count);
            Assert.Equal("asset:audio/radio/the_bureau_track_1.mp3", reviews[0]!["assetId"]!.GetValue<string>());
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("lossless-review-one"))).ToLowerInvariant(),
                template["reviewCopyManifestSha256"]!.GetValue<string>().Length == 64
                    ? reviews[0]!["outputSha256"]!.GetValue<string>()
                    : string.Empty);
            Assert.Equal(
                Sha256(Path.Combine(review, "review-copy-manifest.json")),
                template["reviewCopyManifestSha256"]!.GetValue<string>());
            Assert.All(reviews, row =>
            {
                Assert.Equal("pending", row!["decision"]!.GetValue<string>());
                Assert.Empty(row["reviewedDeviceIds"]!.AsArray());
                Assert.Empty(row["findingIds"]!.AsArray());
                Assert.Equal(
                    string.Join(',', Criteria),
                    string.Join(',', row["criteria"]!.AsArray().Select(criterion => criterion!["criterionId"]!.GetValue<string>())));
                Assert.All(row["criteria"]!.AsArray(), criterion => Assert.Equal("pending", criterion!["result"]!.GetValue<string>()));
            });
            foreach (var field in ConfirmationFields)
            {
                Assert.False(template["confirmations"]![field]!.GetValue<bool>());
            }

            var handoffPath = Path.Combine(review, "listening-handoff.json");
            var handoff = RadioListeningReviewCheck.VerifyInputs(root, review, handoffPath);
            var handoffJson = JsonNode.Parse(File.ReadAllBytes(handoffPath))!.AsObject();
            Assert.Equal(2, handoff.TrackCount);
            Assert.True(handoffJson["passed"]!.GetValue<bool>());
            Assert.True(handoffJson["technicalInputsVerified"]!.GetValue<bool>());
            Assert.Equal("pending", handoffJson["humanListeningStatus"]!.GetValue<string>());
            Assert.False(handoffJson["listeningComplete"]!.GetValue<bool>());
            Assert.False(handoffJson["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.False(handoffJson["releaseApproved"]!.GetValue<bool>());
            Assert.False(handoffJson["exportEligibilityChanged"]!.GetValue<bool>());
            Assert.Equal(
                "asset:audio/radio/the_bureau_track_1.mp3,asset:audio/radio/the_bureau_track_2.mp3",
                string.Join(',', handoffJson["reviewCopySha256ByAssetId"]!.AsObject().Select(pair => pair.Key)));
            AssertNoStaging(review);

            var again = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, output));
            Assert.Contains("overwrite", again.Message, StringComparison.Ordinal);
        }
        finally
        {
            Delete(Path.GetDirectoryName(review)!);
            Delete(root);
        }
    }

    [Fact]
    public void Complete_approval_requires_every_track_criterion_and_device()
    {
        var root = TempDirectory();
        var review = Path.Combine(TempDirectory(), "the_bureau");
        try
        {
            WriteReviewSet(review, reverseOrder: false);
            var template = Completed(Prepare(root, review));
            var record = Path.Combine(review, "listening-review.json");
            WriteJson(record, template);
            var validation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            var evidence = validation.Evidence;

            Assert.Empty(validation.Errors);
            Assert.True(evidence["passed"]!.GetValue<bool>());
            Assert.True(evidence["technicalInputsVerified"]!.GetValue<bool>());
            Assert.True(evidence["listeningComplete"]!.GetValue<bool>());
            Assert.Equal(2, evidence["approvedTrackCount"]!.GetValue<int>());
            Assert.True(evidence["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.False(evidence["releaseApproved"]!.GetValue<bool>());
            Assert.False(evidence["exportEligibilityChanged"]!.GetValue<bool>());
            Assert.Empty(evidence["pendingGates"]!.AsArray());
        }
        finally
        {
            Delete(Path.GetDirectoryName(review)!);
            Delete(root);
        }
    }

    [Fact]
    public void Honest_rejection_and_blocked_decision_do_not_approve_sources()
    {
        var root = TempDirectory();
        var review = Path.Combine(TempDirectory(), "the_bureau");
        try
        {
            WriteReviewSet(review, reverseOrder: false);
            var rejected = Completed(Prepare(root, review));
            var first = rejected["trackReviews"]!.AsArray()[0]!.AsObject();
            first["criteria"]!.AsArray()[4]!["result"] = "fail";
            first["decision"] = "reject-source-replacement";
            first["findingIds"] = new JsonArray("radio-finding-001");
            var record = Path.Combine(review, "listening-review.json");
            WriteJson(record, rejected);
            var validation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);

            Assert.Empty(validation.Errors);
            Assert.True(validation.Evidence["listeningComplete"]!.GetValue<bool>());
            Assert.Equal(1, validation.Evidence["approvedTrackCount"]!.GetValue<int>());
            Assert.Equal(1, validation.Evidence["rejectedTrackCount"]!.GetValue<int>());
            Assert.False(validation.Evidence["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.False(validation.Evidence["releaseApproved"]!.GetValue<bool>());
            Assert.False(validation.Evidence["exportEligibilityChanged"]!.GetValue<bool>());

            var blocked = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            var blockedRow = blocked["trackReviews"]!.AsArray()[1]!.AsObject();
            blockedRow["criteria"]!.AsArray()[0]!["result"] = "blocked";
            blockedRow["decision"] = "blocked";
            blockedRow["findingIds"] = new JsonArray("radio-finding-002");
            WriteJson(record, blocked);
            var blockedValidation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Empty(blockedValidation.Errors);
            Assert.True(blockedValidation.Evidence["listeningComplete"]!.GetValue<bool>());
            Assert.Equal(1, blockedValidation.Evidence["blockedTrackCount"]!.GetValue<int>());
            Assert.False(blockedValidation.Evidence["sourceReplacementApproved"]!.GetValue<bool>());
        }
        finally
        {
            Delete(Path.GetDirectoryName(review)!);
            Delete(root);
        }
    }

    [Fact]
    public void Inconsistent_approval_and_tampered_copy_fail_closed()
    {
        var root = TempDirectory();
        var review = Path.Combine(TempDirectory(), "the_bureau");
        try
        {
            WriteReviewSet(review, reverseOrder: false);
            var recordObject = Completed(Prepare(root, review));
            recordObject["trackReviews"]!.AsArray()[0]!["criteria"]!.AsArray()[0]!["result"] = "fail";
            var record = Path.Combine(review, "listening-review.json");
            WriteJson(record, recordObject);
            var validation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);

            Assert.Contains(validation.Errors, error => error.Contains("cannot approve unless every criterion passes", StringComparison.Ordinal));
            Assert.False(validation.Evidence["listeningComplete"]!.GetValue<bool>());
            Assert.False(validation.Evidence["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.True(validation.Evidence["technicalInputsVerified"]!.GetValue<bool>());

            File.WriteAllBytes(Path.Combine(review, "the_bureau_track_1.review.flac"), "tampered"u8.ToArray());
            var tampered = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(
                tampered.Errors,
                error => error.Contains("size mismatch", StringComparison.Ordinal) || error.Contains("SHA-256 mismatch", StringComparison.Ordinal));
            Assert.False(tampered.Evidence["technicalInputsVerified"]!.GetValue<bool>());
            Assert.False(tampered.Evidence["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.False(tampered.Evidence.ContainsKey("stationId"));
        }
        finally
        {
            Delete(Path.GetDirectoryName(review)!);
            Delete(root);
        }
    }

    [Fact]
    public void Malformed_findings_time_devices_and_confirmations_fail_closed()
    {
        var root = TempDirectory();
        var review = Path.Combine(TempDirectory(), "the_bureau");
        try
        {
            WriteReviewSet(review, reverseOrder: false);
            var recordObject = Completed(Prepare(root, review));
            recordObject["executedUtc"] = "2026-99-99T99:99:99Z";
            recordObject["trackReviews"]!.AsArray()[0]!["findingIds"] = new JsonArray(new JsonObject { ["unexpected"] = true });
            var record = Path.Combine(review, "listening-review.json");
            WriteJson(record, recordObject);
            var validation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);

            Assert.Contains(validation.Errors, error => error.Contains("executedUtc must use", StringComparison.Ordinal));
            Assert.Contains(validation.Errors, error => error.Contains("findingIds must contain", StringComparison.Ordinal));
            Assert.False(validation.Evidence["listeningComplete"]!.GetValue<bool>());
            Assert.False(validation.Evidence["sourceReplacementApproved"]!.GetValue<bool>());

            var calendar = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            calendar["executedUtc"] = "2026-02-31T00:00:00Z";
            WriteJson(record, calendar);
            var calendarValidation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(calendarValidation.Errors, error => error.Contains("executedUtc must use", StringComparison.Ordinal));

            var swapped = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            swapped["trackReviews"]!.AsArray()[0]!["reviewedDeviceIds"] = new JsonArray("speakers", "headphones");
            WriteJson(record, swapped);
            var devices = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(devices.Errors, error => error.Contains("reviewedDeviceIds must be", StringComparison.Ordinal));
            Assert.False(devices.Evidence["sourceReplacementApproved"]!.GetValue<bool>());

            var numeric = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            numeric["confirmations"]!["listenedToEveryTrackInFull"] = 1;
            WriteJson(record, numeric);
            var confirmation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(confirmation.Errors, error => error.Contains("must be a boolean", StringComparison.Ordinal));

            var bareRejection = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            var bareRow = bareRejection["trackReviews"]!.AsArray()[0]!.AsObject();
            bareRow["criteria"]!.AsArray()[0]!["result"] = "pass";
            bareRow["decision"] = "reject-source-replacement";
            WriteJson(record, bareRejection);
            var bare = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(bare.Errors, error => error.Contains("rejection requires at least one failed criterion", StringComparison.Ordinal));
            Assert.Contains(bare.Errors, error => error.Contains("requires a finding ID", StringComparison.Ordinal));

            var unsupported = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            unsupported["trackReviews"]!.AsArray()[0]!["decision"] = "maybe";
            unsupported["trackReviews"]!.AsArray()[0]!["criteria"]!.AsArray()[1]!["result"] = "nope";
            WriteJson(record, unsupported);
            var unsupportedValidation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(unsupportedValidation.Errors, error => error.Contains("decision is unsupported: 'maybe'", StringComparison.Ordinal));
            Assert.Contains(unsupportedValidation.Errors, error => error.Contains("result is unsupported: 'nope'", StringComparison.Ordinal));

            var pending = JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject();
            WriteJson(record, pending);
            var pendingValidation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(pendingValidation.Errors, error => error.Contains("reviewerId must match", StringComparison.Ordinal));
            Assert.False(pendingValidation.Evidence["listeningComplete"]!.GetValue<bool>());
            Assert.False(pendingValidation.Evidence["sourceReplacementApproved"]!.GetValue<bool>());

            var shortRecord = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            shortRecord["trackReviews"]!.AsArray().RemoveAt(1);
            WriteJson(record, shortRecord);
            var shortValidation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(shortValidation.Errors, error => error.Contains("exactly 2 rows", StringComparison.Ordinal));
            Assert.Contains(shortValidation.Errors, error => error.Contains("every exact review copy once", StringComparison.Ordinal));

            var reordered = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            var firstCriteria = reordered["trackReviews"]!.AsArray()[0]!["criteria"]!.AsArray();
            var firstCriterion = firstCriteria[0]!["criterionId"]!.GetValue<string>();
            var secondCriterion = firstCriteria[1]!["criterionId"]!.GetValue<string>();
            firstCriteria[0]!["criterionId"] = secondCriterion;
            firstCriteria[1]!["criterionId"] = firstCriterion;
            WriteJson(record, reordered);
            var reorderedValidation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(reorderedValidation.Errors, error => error.Contains("out of contract order", StringComparison.Ordinal));

            var extra = Completed(JsonNode.Parse(File.ReadAllBytes(Path.Combine(review, "listening-review.json.template")))!.AsObject());
            extra["unexpected"] = true;
            WriteJson(record, extra);
            var extraValidation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Contains(extraValidation.Errors, error => error.Contains("fields must be", StringComparison.Ordinal));
            Assert.False(extraValidation.Evidence["passed"]!.GetValue<bool>());
        }
        finally
        {
            Delete(Path.GetDirectoryName(review)!);
            Delete(root);
        }
    }

    [Fact]
    public void Record_must_stay_inside_the_review_directory()
    {
        var root = TempDirectory();
        var review = Path.Combine(TempDirectory(), "the_bureau");
        try
        {
            WriteReviewSet(review, reverseOrder: false);
            var record = Completed(Prepare(root, review));
            var detached = Path.Combine(Path.GetDirectoryName(review)!, "detached-listening-review.json");
            WriteJson(detached, record);
            var validation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, detached);

            Assert.Equal(["radio listening record must be directly inside its review directory"], validation.Errors);
            Assert.False(validation.Evidence["technicalInputsVerified"]!.GetValue<bool>());
            Assert.False(validation.Evidence["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.False(validation.Evidence.ContainsKey("pendingGates"));
        }
        finally
        {
            Delete(Path.GetDirectoryName(review)!);
            Delete(root);
        }
    }

    [Fact]
    public void Paths_manifest_limits_and_links_fail_closed()
    {
        var root = TempDirectory();
        var outside = TempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "assets", "audio"));
            var assetsDirectory = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, Path.Combine(root, "assets"), Path.Combine(root, "assets", "template.json")));
            Assert.Contains("public assets", assetsDirectory.Message, StringComparison.Ordinal);
            var repositoryDirectory = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, root, Path.Combine(root, "template.json")));
            Assert.Contains("ignored TestResults", repositoryDirectory.Message, StringComparison.Ordinal);
            var assetReview = Path.Combine(root, "assets", "audio", "the_bureau");
            WriteReviewSet(assetReview, reverseOrder: false);
            var assetFailure = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, assetReview, Path.Combine(assetReview, "template.json")));
            Assert.Contains("public assets", assetFailure.Message, StringComparison.Ordinal);

            var notes = Path.Combine(root, "notes");
            WriteReviewSet(notes, reverseOrder: false);
            var notesFailure = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, notes, Path.Combine(notes, "template.json")));
            Assert.Contains("ignored TestResults", notesFailure.Message, StringComparison.Ordinal);

            var allowed = Path.Combine(root, "TestResults", "radio-review", "the_bureau");
            WriteReviewSet(allowed, reverseOrder: false);
            var allowedOutput = Path.Combine(allowed, "listening-review.json.template");
            var prepared = RadioListeningReviewCheck.PrepareTemplate(root, allowed, allowedOutput);
            Assert.Equal(2, prepared.TrackCount);
            var elsewhere = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, allowed, Path.Combine(outside, "template.json")));
            Assert.Contains("directly inside", elsewhere.Message, StringComparison.Ordinal);

            var handoff = Path.Combine(allowed, "listening-handoff.json");
            RadioListeningReviewCheck.VerifyInputs(root, allowed, handoff);
            RadioListeningReviewCheck.VerifyInputs(root, allowed, handoff);
            Assert.True(File.Exists(handoff));
            var manifestOutput = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.VerifyInputs(root, allowed, Path.Combine(allowed, "review-copy-manifest.json")));
            Assert.Contains("cannot overwrite an input record", manifestOutput.Message, StringComparison.Ordinal);
            var textOutput = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.VerifyInputs(root, allowed, Path.Combine(outside, "handoff.txt")));
            Assert.Contains(".json", textOutput.Message, StringComparison.Ordinal);
            var insideNotes = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.VerifyInputs(root, allowed, Path.Combine(root, "notes", "handoff.json")));
            Assert.Contains("ignored TestResults", insideNotes.Message, StringComparison.Ordinal);

            var outsideReview = Path.Combine(outside, "station");
            WriteReviewSet(outsideReview, reverseOrder: false, mutate: manifest =>
            {
                manifest["note"] = "NaN";
                manifest["summary"]!["extra"] = true;
            });
            var outsideTemplate = RadioListeningReviewCheck.PrepareTemplate(
                root,
                outsideReview,
                Path.Combine(outsideReview, "listening-review.json.template"));
            Assert.Equal("the_bureau", outsideTemplate.StationId);

            AssertMessage(
                root,
                outsideReview,
                "radio review-copy manifest must be an object",
                _ => File.WriteAllText(Path.Combine(outsideReview, "review-copy-manifest.json"), "[1]\n"));
            AssertMessage(
                root,
                outsideReview,
                "duplicate JSON field: schemaVersion",
                _ => File.WriteAllText(
                    Path.Combine(outsideReview, "review-copy-manifest.json"),
                    "{\"schemaVersion\":1,\"schemaVersion\":1}\n"));
            AssertMessage(
                root,
                outsideReview,
                "non-finite JSON number: NaN",
                _ => File.WriteAllText(Path.Combine(outsideReview, "review-copy-manifest.json"), "{\"schemaVersion\": NaN}\n"));
            AssertMessage(
                root,
                outsideReview,
                "non-finite JSON number: Infinity",
                _ => File.WriteAllText(Path.Combine(outsideReview, "review-copy-manifest.json"), "{\"schemaVersion\": Infinity}\n"));
            AssertMessage(
                root,
                outsideReview,
                "non-finite JSON number: -Infinity",
                _ => File.WriteAllText(Path.Combine(outsideReview, "review-copy-manifest.json"), "{\"schemaVersion\": -Infinity}\n"));
            WriteReviewSet(outsideReview, reverseOrder: false);
            var limited = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, outsideReview, Path.Combine(outsideReview, "tiny.json"), 32));
            Assert.Contains("exceeds the 32-byte limit", limited.Message, StringComparison.Ordinal);
            File.WriteAllBytes(Path.Combine(outsideReview, "review-copy-manifest.json"), [0xFF, 0xFE, 0x61]);
            var unreadable = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, outsideReview, Path.Combine(outsideReview, "bad.json")));
            Assert.Contains("unreadable radio review-copy manifest", unreadable.Message, StringComparison.Ordinal);

            WriteReviewSet(outsideReview, reverseOrder: false);
            File.Delete(Path.Combine(outsideReview, "review-copy-manifest.json"));
            var missing = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, outsideReview, Path.Combine(outsideReview, "missing.json")));
            Assert.Contains("missing regular radio review-copy manifest", missing.Message, StringComparison.Ordinal);

            var link = Path.Combine(outside, "linked-manifest");
            Directory.CreateDirectory(link);
            WriteReviewSet(link, reverseOrder: false);
            var manifestLink = Path.Combine(link, "review-copy-manifest.json");
            var manifestTarget = Path.Combine(outside, "manifest-target.json");
            File.Move(manifestLink, manifestTarget);
            if (TryCreateFileLink(manifestLink, manifestTarget))
            {
                var linked = Assert.Throws<RadioListeningReviewException>(() =>
                    RadioListeningReviewCheck.PrepareTemplate(root, link, Path.Combine(link, "template.json")));
                Assert.Contains("missing regular radio review-copy manifest", linked.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Delete(root);
            Delete(outside);
        }
    }

    [Fact]
    public void Review_rows_reject_incomplete_identity_and_reparse_directories()
    {
        var root = TempDirectory();
        var outside = TempDirectory();
        try
        {
            var review = Path.Combine(outside, "station");
            AssertRow(root, review, "stationId is invalid", manifest => manifest["stationId"] = "The_Bureau");
            AssertRow(root, review, "must contain 1 to 128 tracks", manifest => manifest["reviewCopies"] = new JsonArray());
            AssertRow(root, review, "summary track count is invalid", manifest => manifest["summary"]!["trackCount"] = 1);
            AssertRow(root, review, "complete technical pass", manifest => manifest["summary"]!["technicalFailureCount"] = 1);
            AssertRow(root, review, "must be an object", manifest => manifest["reviewCopies"]!.AsArray()[0] = 1);
            AssertRow(root, review, "unique radio asset ID", manifest => manifest["reviewCopies"]!.AsArray()[1]!["assetId"] = "asset:audio/radio/the_bureau_track_1.mp3");
            AssertRow(root, review, "unique review FLAC file name", manifest => manifest["reviewCopies"]!.AsArray()[0]!["outputFile"] = "nested/the_bureau_track_1.review.flac");
            AssertRow(root, review, "unique review FLAC file name", manifest => manifest["reviewCopies"]!.AsArray()[0]!["outputFile"] = "the_bureau_track_1.review.flac\\extra");
            AssertRow(root, review, "SHA-256 digest", manifest => manifest["reviewCopies"]!.AsArray()[0]!["outputSha256"] = "ABCD");
            AssertRow(root, review, "positive integer", manifest => manifest["reviewCopies"]!.AsArray()[0]!["outputBytes"] = 0);
            AssertRow(root, review, "positive integer", manifest => manifest["reviewCopies"]!.AsArray()[0]!["outputBytes"] = true);
            AssertRow(root, review, "positive integer", manifest => manifest["reviewCopies"]!.AsArray()[0]!["outputBytes"] = JsonNode.Parse("1.5"));
            AssertRow(root, review, "complete technical pass", manifest => manifest["reviewCopies"]!.AsArray()[0]!["technicalPass"] = false);
            AssertRow(root, review, "complete technical pass", manifest => manifest["reviewCopies"]!.AsArray()[0]!["stationId"] = "other_station");
            AssertRow(root, review, "complete technical pass", manifest => manifest["reviewCopies"]!.AsArray()[0]!["failures"] = new JsonArray("edge"));
            AssertRow(root, review, "must contain 1 to 128 tracks", manifest =>
            {
                var many = new JsonArray();
                for (var index = 0; index < 129; index++)
                {
                    many.Add(new JsonObject());
                }

                manifest["reviewCopies"] = many;
            });
            AssertRow(root, review, "technicalPass must be True", manifest => manifest["technicalPass"] = 1);
            AssertRow(root, review, "releaseApproved must be False", manifest => manifest["releaseApproved"] = true);

            WriteReviewSet(review, reverseOrder: false);
            var copy = Path.Combine(review, "the_bureau_track_1.review.flac");
            var sameSize = Encoding.ASCII.GetBytes("LOSSLESS-REVIEW-ONE");
            File.WriteAllBytes(copy, sameSize);
            var mismatch = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json")));
            Assert.Contains("SHA-256 mismatch", mismatch.Message, StringComparison.Ordinal);
            WriteReviewSet(review, reverseOrder: false);
            File.Delete(Path.Combine(review, "the_bureau_track_2.review.flac"));
            var missingCopy = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json")));
            Assert.Contains("missing regular review copy", missingCopy.Message, StringComparison.Ordinal);

            var target = Path.Combine(outside, "real-station");
            WriteReviewSet(target, reverseOrder: false);
            var junction = Path.Combine(outside, "junction-station");
            if (TryCreateDirectoryLink(junction, target))
            {
                try
                {
                    var reparse = Assert.Throws<RadioListeningReviewException>(() =>
                        RadioListeningReviewCheck.PrepareTemplate(root, junction, Path.Combine(junction, "template.json")));
                    Assert.Contains("regular directory path", reparse.Message, StringComparison.Ordinal);
                }
                finally
                {
                    if (Directory.Exists(junction))
                    {
                        Directory.Delete(junction, recursive: false);
                    }
                }
            }
        }
        finally
        {
            Delete(root);
            Delete(outside);
        }
    }

    [Fact]
    public void Contract_edges_fail_closed_without_changing_release_state()
    {
        var root = TempDirectory();
        var outside = TempDirectory();
        var review = Path.Combine(outside, "the_bureau");
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json"), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RadioListeningReviewCheck.VerifyInputs(root, review, Path.Combine(outside, "handoff.json"), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RadioListeningReviewCheck.ValidateListeningRecord(root, review, Path.Combine(review, "record.json"), 0));

            AssertRow(root, review, "unique review FLAC", manifest =>
                manifest["reviewCopies"]!.AsArray()[1]!["outputFile"] = "the_bureau_track_1.review.flac");
            AssertRow(root, review, "summary track count is invalid", manifest =>
                manifest["summary"] = new JsonArray());
            AssertRow(root, review, "schemaVersion must be 1", manifest =>
                manifest["schemaVersion"] = JsonNode.Parse("1.0"));
            AssertRow(root, review, "positive integer", manifest =>
                manifest["reviewCopies"]!.AsArray()[0]!["outputBytes"] = JsonNode.Parse("99999999999999999999"));
            AssertRow(root, review, "humanListeningRequired must be True", manifest =>
                manifest["humanListeningRequired"] = false);
            AssertRow(root, review, "sourceBytesModified must be False", manifest =>
                manifest["sourceBytesModified"] = true);
            AssertRow(root, review, "modifiedSourcePaths must be []", manifest =>
                manifest["modifiedSourcePaths"] = new JsonArray("assets/audio/radio/track.mp3"));
            AssertRow(root, review, "kind must be 'vibesnake-radio-review-copy-set-v1'", manifest =>
                manifest["kind"] = "other");
            AssertRow(root, review, "unique review FLAC", manifest =>
                manifest["reviewCopies"]!.AsArray()[0]!["outputFile"] = "bad\nname.review.flac");
            AssertRow(root, review, "complete technical pass", manifest =>
                manifest["reviewCopies"]!.AsArray()[0]!["failures"] = new JsonObject());
            AssertRow(root, review, "complete technical pass", manifest =>
                manifest["summary"]!.AsObject().Remove("technicalPassCount"));

            WriteReviewSet(review, reverseOrder: false, manifest => manifest["note"] = "hear \"NaN\" clearly");
            var quoted = RadioListeningReviewCheck.PrepareTemplate(
                root,
                review,
                Path.Combine(review, "quoted-template.json"));
            Assert.Equal(2, quoted.TrackCount);

            File.WriteAllText(Path.Combine(review, "review-copy-manifest.json"), "{\"schemaVersion\":xNaN}\n");
            var adjacent = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json")));
            Assert.Contains("unreadable", adjacent.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("non-finite", adjacent.Message, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(review, "review-copy-manifest.json"), "null\n");
            var nullRoot = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json")));
            Assert.Contains("unreadable", nullRoot.Message, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(review, "review-copy-manifest.json"), "{\"schemaVersion\":1,}\n");
            var trailing = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json")));
            Assert.Contains("unreadable", trailing.Message, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(review, "review-copy-manifest.json"), "{\"schemaVersion\":1} /* note */\n");
            var comment = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json")));
            Assert.Contains("unreadable", comment.Message, StringComparison.Ordinal);

            WriteReviewSet(review, reverseOrder: false);
            var directoryTemplate = Path.Combine(review, "template-directory");
            Directory.CreateDirectory(directoryTemplate);
            var directoryOutput = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, review, directoryTemplate));
            Assert.Contains("overwrite", directoryOutput.Message, StringComparison.Ordinal);

            var blockedOutput = Path.Combine(outside, "blocked-output.json");
            Directory.CreateDirectory(blockedOutput);
            var writeFailure = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.VerifyInputs(root, review, blockedOutput));
            Assert.Contains("could not write radio listening evidence", writeFailure.Message, StringComparison.Ordinal);
            AssertNoStaging(outside);

            var upper = Path.Combine(outside, "Handoff.JSON");
            RadioListeningReviewCheck.VerifyInputs(root, review, upper);
            Assert.Equal(
                "vibesnake-radio-listening-handoff-v1",
                JsonNode.Parse(File.ReadAllBytes(upper))!["kind"]!.GetValue<string>());

            var extra = Path.Combine(root, "TestResults", "radio-review-extra", "the_bureau");
            WriteReviewSet(extra, reverseOrder: false);
            var extraFailure = Assert.Throws<RadioListeningReviewException>(() =>
                RadioListeningReviewCheck.PrepareTemplate(root, extra, Path.Combine(extra, "template.json")));
            Assert.Contains("ignored TestResults", extraFailure.Message, StringComparison.Ordinal);

            if (OperatingSystem.IsWindows())
            {
                var cased = Path.Combine(root, "TestResults", "Radio-Review", "the_bureau");
                WriteReviewSet(cased, reverseOrder: false);
                var casedPreparation = RadioListeningReviewCheck.PrepareTemplate(
                    root,
                    cased,
                    Path.Combine(cased, "listening-review.json.template"));
                Assert.Equal("the_bureau", casedPreparation.StationId);
                var casedManifest = Assert.Throws<RadioListeningReviewException>(() =>
                    RadioListeningReviewCheck.VerifyInputs(
                        root,
                        cased,
                        Path.Combine(cased, "Review-Copy-Manifest.JSON")));
                Assert.Contains("cannot overwrite an input record", casedManifest.Message, StringComparison.Ordinal);
            }

            var template = Prepare(root, review);
            var record = Path.Combine(review, "listening-review.json");
            void RejectRecord(JsonNode node, string fragment, bool inputsVerified = true)
            {
                WriteJson(record, node);
                var validation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
                Assert.Contains(validation.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
                Assert.Equal(inputsVerified, validation.Evidence["technicalInputsVerified"]!.GetValue<bool>());
                Assert.False(validation.Evidence["sourceReplacementApproved"]!.GetValue<bool>());
                Assert.False(validation.Evidence["releaseApproved"]!.GetValue<bool>());
                Assert.False(validation.Evidence["exportEligibilityChanged"]!.GetValue<bool>());
            }

            var held = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            held["trackReviews"]!.AsArray()[0]!["decision"] = "pending";
            WriteJson(record, held);
            var heldValidation = RadioListeningReviewCheck.ValidateListeningRecord(root, review, record);
            Assert.Empty(heldValidation.Errors);
            Assert.True(heldValidation.Evidence["passed"]!.GetValue<bool>());
            Assert.False(heldValidation.Evidence["listeningComplete"]!.GetValue<bool>());
            Assert.False(heldValidation.Evidence["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.False(heldValidation.Evidence["releaseApproved"]!.GetValue<bool>());
            Assert.False(heldValidation.Evidence["exportEligibilityChanged"]!.GetValue<bool>());
            Assert.NotEmpty(heldValidation.Evidence["pendingGates"]!.AsArray());

            var identity = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            identity["trackReviews"]!.AsArray()[0]!["outputFile"] = "the_bureau_track_2.review.flac";
            RejectRecord(identity, "output identity does not match");

            var duplicateAsset = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            duplicateAsset["trackReviews"]!.AsArray()[1]!["assetId"] =
                duplicateAsset["trackReviews"]!.AsArray()[0]!["assetId"]!.GetValue<string>();
            RejectRecord(duplicateAsset, "must identify one unique exact review copy");

            var unknownAsset = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            unknownAsset["trackReviews"]!.AsArray()[0]!["assetId"] = "asset:audio/radio/missing.mp3";
            RejectRecord(unknownAsset, "must identify one unique exact review copy");

            var shortCriteria = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            shortCriteria["trackReviews"]!.AsArray()[0]!["criteria"] = new JsonArray(new JsonObject
            {
                ["criterionId"] = "complete-playback",
                ["result"] = "pass",
            });
            RejectRecord(shortCriteria, "must contain the exact 6 criteria");

            var extraCriterion = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            extraCriterion["trackReviews"]!.AsArray()[0]!["criteria"]!.AsArray()[0]!["note"] = true;
            RejectRecord(extraCriterion, "fields must be");

            var blocked = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            var blockedRow = blocked["trackReviews"]!.AsArray()[0]!.AsObject();
            blockedRow["decision"] = "blocked";
            blockedRow["findingIds"] = new JsonArray("radio-finding-003");
            RejectRecord(blocked, "blocked decision requires at least one blocked criterion");

            var duplicateFindings = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            var findingRow = duplicateFindings["trackReviews"]!.AsArray()[0]!.AsObject();
            findingRow["criteria"]!.AsArray()[0]!["result"] = "fail";
            findingRow["decision"] = "reject-source-replacement";
            findingRow["findingIds"] = new JsonArray("radio-finding-001", "radio-finding-001");
            RejectRecord(duplicateFindings, "findingIds must contain unique");

            var confirmationArray = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            confirmationArray["confirmations"] = new JsonArray();
            RejectRecord(confirmationArray, "listening record confirmations must be an object");

            var objectDecision = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            objectDecision["trackReviews"]!.AsArray()[0]!["decision"] = new JsonObject { ["value"] = true };
            RejectRecord(objectDecision, "decision is unsupported: None");

            var schema = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            schema["schemaVersion"] = 2;
            RejectRecord(schema, "schemaVersion must be 1");

            var kind = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            kind["kind"] = "other";
            RejectRecord(kind, "kind is invalid");

            var station = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            station["stationId"] = "other_station";
            RejectRecord(station, "stationId does not match");

            var digest = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            digest["reviewCopyManifestSha256"] = new string('0', 64);
            RejectRecord(digest, "manifest SHA-256 does not match");

            var rowType = Completed(JsonNode.Parse(template.ToJsonString())!.AsObject());
            rowType["trackReviews"]!.AsArray()[0] = 1;
            RejectRecord(rowType, "must be an object");

            WriteJson(record, new JsonArray());
            RejectRecord(new JsonArray(), "listening record must be an object");
        }
        finally
        {
            Delete(root);
            Delete(outside);
        }
    }

    [Fact]
    public void Command_prepares_verifies_reviews_and_rejects_bad_invocation()
    {
        var root = TempDirectory();
        var review = Path.Combine(TempDirectory(), "the_bureau");
        try
        {
            WriteReviewSet(review, reverseOrder: false);
            var template = Path.Combine(review, "listening-review.json.template");
            var output = new StringWriter();
            var error = new StringWriter();
            var code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "prepare-template", review, template],
                output,
                error);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(
                "Radio listening template prepared: station=the_bureau tracks=2 listening=pending output="
                + Path.GetFullPath(template)
                + Environment.NewLine,
                output.ToString());

            var handoff = Path.Combine(review, "listening-handoff.json");
            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "verify-inputs", review, handoff],
                output,
                error);
            Assert.Equal(0, code);
            Assert.Equal(
                "Radio listening handoff verified: station=the_bureau tracks=2 listening=pending" + Environment.NewLine,
                output.ToString());

            var recordObject = Completed(JsonNode.Parse(File.ReadAllBytes(template))!.AsObject());
            recordObject["trackReviews"]!.AsArray()[0]!["criteria"]!.AsArray()[4]!["result"] = "fail";
            recordObject["trackReviews"]!.AsArray()[0]!["decision"] = "reject-source-replacement";
            recordObject["trackReviews"]!.AsArray()[0]!["findingIds"] = new JsonArray("radio-finding-004");
            var record = Path.Combine(review, "listening-review.json");
            WriteJson(record, recordObject);
            var decision = Path.Combine(review, "listening-decision.json");
            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "review-record", review, record, decision, "require-approved"],
                output,
                error);
            Assert.Equal(1, code);
            Assert.Equal(
                "Radio listening record is valid but does not approve every source replacement." + Environment.NewLine,
                error.ToString());
            var decisionJson = JsonNode.Parse(File.ReadAllBytes(decision))!.AsObject();
            Assert.True(decisionJson["passed"]!.GetValue<bool>());
            Assert.False(decisionJson["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.False(decisionJson["releaseApproved"]!.GetValue<bool>());
            Assert.False(decisionJson["exportEligibilityChanged"]!.GetValue<bool>());
            AssertCanonical(File.ReadAllBytes(decision));

            var approved = Completed(JsonNode.Parse(File.ReadAllBytes(template))!.AsObject());
            WriteJson(record, approved);
            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "review-record", review, record, decision, "require-approved"],
                output,
                error);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Contains("complete=true source_replacement_approved=true", output.ToString(), StringComparison.Ordinal);
            decisionJson = JsonNode.Parse(File.ReadAllBytes(decision))!.AsObject();
            Assert.True(decisionJson["sourceReplacementApproved"]!.GetValue<bool>());
            Assert.False(decisionJson["releaseApproved"]!.GetValue<bool>());
            Assert.False(decisionJson["exportEligibilityChanged"]!.GetValue<bool>());

            var held = Completed(JsonNode.Parse(File.ReadAllBytes(template))!.AsObject());
            held["trackReviews"]!.AsArray()[0]!["decision"] = "pending";
            WriteJson(record, held);
            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "review-record", review, record, decision],
                output,
                error);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Contains(
                "complete=false source_replacement_approved=false",
                output.ToString(),
                StringComparison.Ordinal);
            decisionJson = JsonNode.Parse(File.ReadAllBytes(decision))!.AsObject();
            Assert.True(decisionJson["passed"]!.GetValue<bool>());
            Assert.False(decisionJson["releaseApproved"]!.GetValue<bool>());
            Assert.False(decisionJson["exportEligibilityChanged"]!.GetValue<bool>());

            recordObject = Completed(JsonNode.Parse(File.ReadAllBytes(template))!.AsObject());
            recordObject["reviewerId"] = "radio-reviewer-REPLACE";
            WriteJson(record, recordObject);
            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "review-record", review, record, decision],
                output,
                error);
            Assert.Equal(1, code);
            Assert.StartsWith("Radio listening record validation failed:", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("reviewerId must match", error.ToString(), StringComparison.Ordinal);
            Assert.False(JsonNode.Parse(File.ReadAllBytes(decision))!["passed"]!.GetValue<bool>());

            var textDecision = Path.Combine(Path.GetDirectoryName(review)!, "decision.txt");
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "review-record", review, record, textDecision],
                new StringWriter(),
                error);
            Assert.Equal(1, code);
            Assert.StartsWith("Radio listening decision output failed:", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("validation failed", error.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(textDecision));

            var absent = Path.Combine(Path.GetDirectoryName(review)!, "absent-station");
            Directory.CreateDirectory(absent);
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "prepare-template", absent, Path.Combine(absent, "template.json")],
                new StringWriter(),
                error);
            Assert.Equal(1, code);
            Assert.StartsWith("Radio listening template preparation failed:", error.ToString(), StringComparison.Ordinal);

            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "verify-inputs", review, Path.Combine(review, "handoff.txt")],
                new StringWriter(),
                error);
            Assert.Equal(1, code);
            Assert.StartsWith("Radio listening handoff verification failed:", error.ToString(), StringComparison.Ordinal);
            Assert.Contains(".json file name", error.ToString(), StringComparison.Ordinal);

            var invalidDecision = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["radio-listening", root, "review-record", review, record, " "],
                    new StringWriter(),
                    invalidDecision));
            Assert.Contains("Radio listening path is invalid.", invalidDecision.ToString(), StringComparison.Ordinal);

            var usageError = new StringWriter();
            Assert.Equal(2, RepositoryCheckCommand.Run(["radio-listening"], new StringWriter(), usageError));
            Assert.Contains(
                "RepositoryChecks radio-listening <repository-root> prepare-template <review-directory> <template-path>",
                usageError.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["radio-listening", root, "prepare-template", review, template, "require-approved"],
                    new StringWriter(),
                    new StringWriter()));
            var invalidRoot = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["radio-listening", "bad\0root", "prepare-template", review, template],
                    new StringWriter(),
                    invalidRoot));
            Assert.Contains("Repository root is invalid.", invalidRoot.ToString(), StringComparison.Ordinal);
            var invalidPath = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["radio-listening", root, "verify-inputs", " ", handoff],
                    new StringWriter(),
                    invalidPath));
            Assert.Contains("Radio listening path is invalid.", invalidPath.ToString(), StringComparison.Ordinal);

            var assetReview = Path.Combine(root, "assets", "station");
            WriteReviewSet(assetReview, reverseOrder: false);
            var assetRecord = Path.Combine(assetReview, "listening-review.json");
            WriteJson(assetRecord, new JsonObject { ["schemaVersion"] = 1 });
            var assetDecision = Path.Combine(Path.GetDirectoryName(review)!, "asset-decision.json");
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(
                ["radio-listening", root, "review-record", assetReview, assetRecord, assetDecision],
                new StringWriter(),
                error);
            Assert.Equal(1, code);
            Assert.Contains("public assets", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("Radio listening record validation failed:", error.ToString(), StringComparison.Ordinal);
            Assert.False(JsonNode.Parse(File.ReadAllBytes(assetDecision))!["technicalInputsVerified"]!.GetValue<bool>());
            Assert.False(JsonNode.Parse(File.ReadAllBytes(assetDecision))!["exportEligibilityChanged"]!.GetValue<bool>());
        }
        finally
        {
            Delete(Path.GetDirectoryName(review)!);
            Delete(root);
        }
    }

    private static JsonObject Prepare(string root, string review)
    {
        var output = Path.Combine(review, "listening-review.json.template");
        RadioListeningReviewCheck.PrepareTemplate(root, review, output);
        return JsonNode.Parse(File.ReadAllBytes(output))!.AsObject();
    }

    private static JsonObject Completed(JsonObject template)
    {
        template["reviewerId"] = "radio-reviewer-001";
        template["executedUtc"] = "2026-08-20T12:00:00Z";
        foreach (var review in template["trackReviews"]!.AsArray())
        {
            var row = review!.AsObject();
            row["reviewedDeviceIds"] = new JsonArray("headphones", "speakers");
            foreach (var criterion in row["criteria"]!.AsArray())
            {
                criterion!["result"] = "pass";
            }

            row["decision"] = "approve-source-replacement";
        }

        var confirmations = template["confirmations"]!.AsObject();
        foreach (var field in ConfirmationFields)
        {
            confirmations[field] = true;
        }

        return template;
    }

    private static void AssertRow(string root, string review, string fragment, Action<JsonObject> mutate)
    {
        WriteReviewSet(review, reverseOrder: false, mutate: mutate);
        var failure = Assert.Throws<RadioListeningReviewException>(() =>
            RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json")));
        Assert.Contains(fragment, failure.Message, StringComparison.Ordinal);
    }

    private static void AssertMessage(string root, string review, string fragment, Action<string> arrange)
    {
        arrange(review);
        var failure = Assert.Throws<RadioListeningReviewException>(() =>
            RadioListeningReviewCheck.PrepareTemplate(root, review, Path.Combine(review, "template.json")));
        Assert.Contains(fragment, failure.Message, StringComparison.Ordinal);
    }

    private static void WriteReviewSet(string path, bool reverseOrder, Action<JsonObject>? mutate = null)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
        var rows = new JsonArray();
        var contents = new (string Name, byte[] Bytes)[]
        {
            ("the_bureau_track_1.review.flac", "lossless-review-one"u8.ToArray()),
            ("the_bureau_track_2.review.flac", "lossless-review-two"u8.ToArray()),
        };
        if (reverseOrder)
        {
            Array.Reverse(contents);
        }

        foreach (var (name, bytes) in contents)
        {
            File.WriteAllBytes(Path.Combine(path, name), bytes);
            var asset = name.Replace(".review.flac", ".mp3", StringComparison.Ordinal);
            rows.Add(new JsonObject
            {
                ["assetId"] = "asset:audio/radio/" + asset,
                ["outputFile"] = name,
                ["outputSha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                ["outputBytes"] = bytes.Length,
                ["stationId"] = "the_bureau",
                ["technicalPass"] = true,
                ["failures"] = new JsonArray(),
            });
        }

        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-radio-review-copy-set-v1",
            ["stationId"] = "the_bureau",
            ["technicalPass"] = true,
            ["releaseApproved"] = false,
            ["sourceReplacementApproved"] = false,
            ["exportEligibilityChanged"] = false,
            ["humanListeningRequired"] = true,
            ["humanListeningStatus"] = "pending",
            ["sourceBytesModified"] = false,
            ["modifiedSourcePaths"] = new JsonArray(),
            ["summary"] = new JsonObject
            {
                ["trackCount"] = 2,
                ["technicalPassCount"] = 2,
                ["technicalFailureCount"] = 0,
            },
            ["reviewCopies"] = rows,
        };
        mutate?.Invoke(manifest);
        WriteJson(Path.Combine(path, "review-copy-manifest.json"), manifest);
    }

    private static void WriteJson(string path, JsonNode node)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(node, InputJson);
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            Array.Resize(ref bytes, bytes.Length + 1);
            bytes[^1] = (byte)'\n';
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void AssertCanonical(byte[] bytes)
    {
        Assert.NotEmpty(bytes);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
    }

    private static void AssertNoStaging(string directory)
    {
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(directory),
            path => Path.GetFileName(path).Contains(".staging.", StringComparison.Ordinal));
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool TryCreateDirectoryLink(string linkPath, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c mklink /J \"" + linkPath + "\" \"" + target + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }

        try
        {
            Directory.CreateSymbolicLink(linkPath, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryCreateFileLink(string linkPath, string target)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-radio-listening-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
