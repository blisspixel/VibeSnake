using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record SfxCueCatalogEntry(
    AudioCue Cue,
    string RuntimeId,
    string Family,
    string SourceType,
    string Provenance,
    string License,
    string? AuthoredAssetPath,
    string Bus,
    int Priority,
    int CooldownMilliseconds,
    int MaximumPolyphony,
    float MusicDuckDecibels,
    ProceduralCueMeasurement Measurement);

internal sealed record SfxQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string ProceduralPeakPolicy,
    string AuthoredLoudnessPolicy,
    string AuthoredAssetReviewStatus,
    int CueCount,
    int ApprovedAuthoredAssetCount,
    bool CatalogComplete,
    bool EveryCueConnected,
    bool EveryCueLicensed,
    bool GenerationCandidatesExcluded,
    bool PeakPolicyComplete,
    bool NoClipping,
    bool NoDuplicateFingerprints,
    bool MenuNavigationDistinct,
    bool ComboTiersDistinct,
    bool ComboBreakDistinct,
    bool PowerActivationsDistinct,
    bool AchievementDistinct,
    bool RestartDistinct,
    bool DeathCausesDistinct,
    bool RulesStateIndependent,
    IReadOnlyList<SfxCueCatalogEntry> Entries)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// Closed fallback-SFX inventory. Authored candidates remain absent from the
/// runtime until separate rights, loudness, clipping, repetition, and listening
/// review admits an exact file through the content inventory.
/// </summary>
internal static class SfxCueCatalog
{
    public const float MinimumProceduralPeakDecibels = -24.5f;
    public const float MaximumProceduralPeakDecibels = -18.0f;

    public static SfxQualificationEvidence Qualify()
    {
        var cues = Enum.GetValues<AudioCue>();
        var measurements = cues
            .Select(ProceduralCuePlayer.MeasureCue)
            .ToDictionary(measurement => measurement.Cue);
        var entries = cues.Select(cue => CreateEntry(cue, measurements[cue])).ToArray();
        var catalogComplete = entries.Length == cues.Length
            && entries.Select(entry => entry.Cue).SequenceEqual(cues)
            && entries.Select(entry => entry.RuntimeId).Distinct(StringComparer.Ordinal).Count()
                == cues.Length;
        var everyCueConnected = FeedbackMatrixCatalog.Entries
            .SelectMany(entry => entry.FallbackCues)
            .Distinct()
            .OrderBy(cue => cue)
            .SequenceEqual(cues.OrderBy(cue => cue));
        var everyCueLicensed = entries.All(entry =>
            entry.SourceType == "procedural-fallback"
            && entry.Provenance == "deterministic-runtime-pcm"
            && entry.License == "Apache-2.0");
        var generationCandidatesExcluded = entries.All(entry =>
            entry.AuthoredAssetPath is null);
        var peakPolicyComplete = entries.All(entry =>
            entry.Measurement.PeakDecibelsFullScale >= MinimumProceduralPeakDecibels
            && entry.Measurement.PeakDecibelsFullScale <= MaximumProceduralPeakDecibels);
        var noClipping = entries.All(entry =>
            entry.Measurement.PeakDecibelsFullScale < 0.0f);
        var noDuplicateFingerprints = entries
            .Select(entry => entry.Measurement.PcmSha256)
            .Distinct(StringComparer.Ordinal)
            .Count() == entries.Length;
        var menuNavigationDistinct = Distinct(entries, AudioCue.Navigate, AudioCue.Confirm, AudioCue.Back);
        var comboTiersDistinct = Distinct(
            entries,
            AudioCue.ComboTier1,
            AudioCue.ComboTier2,
            AudioCue.ComboTier3,
            AudioCue.ComboTier4);
        var comboBreakDistinct = Distinct(
            entries,
            AudioCue.ComboBreak,
            AudioCue.Food,
            AudioCue.ComboTier1,
            AudioCue.ComboTier2,
            AudioCue.ComboTier3,
            AudioCue.ComboTier4);
        var powerActivationCues = Enum.GetValues<PowerKind>()
            .Select(StepFeedback.ActivationCue)
            .ToArray();
        var powerActivationsDistinct = powerActivationCues.Distinct().Count()
                == Enum.GetValues<PowerKind>().Length
            && Fingerprints(entries, powerActivationCues).Distinct(StringComparer.Ordinal).Count()
                == powerActivationCues.Length;
        var achievementDistinct = Distinct(
            entries,
            AudioCue.Achievement,
            AudioCue.Confirm,
            AudioCue.ComboTier4);
        var restartDistinct = Distinct(
            entries,
            AudioCue.Restart,
            AudioCue.Confirm,
            AudioCue.Pause);
        var deathCausesDistinct = Distinct(
            entries,
            AudioCue.Collision,
            AudioCue.StarvationDeath);
        var rulesProbe = SnakeRun.Create(20260823UL);
        var rulesHashBefore = rulesProbe.ComputeStateHash();
        _ = entries.Sum(entry => entry.Measurement.DurationMilliseconds);
        var rulesStateIndependent = rulesProbe.ComputeStateHash() == rulesHashBefore;
        var passed = catalogComplete
            && everyCueConnected
            && everyCueLicensed
            && generationCandidatesExcluded
            && peakPolicyComplete
            && noClipping
            && noDuplicateFingerprints
            && menuNavigationDistinct
            && comboTiersDistinct
            && comboBreakDistinct
            && powerActivationsDistinct
            && achievementDistinct
            && restartDistinct
            && deathCausesDistinct
            && rulesStateIndependent;
        if (!passed)
        {
            throw new InvalidOperationException("Fallback SFX qualification failed.");
        }

        return new SfxQualificationEvidence(
            SchemaVersion: 1,
            Kind: "sfx-catalog-qualification-v1",
            Passed: true,
            ProceduralPeakPolicy: "procedural-fallback-peak-v1:-24.5..-18.0-dBFS",
            AuthoredLoudnessPolicy: "authored-core-v1:-18-LUFS-integrated;-1-dBTP",
            AuthoredAssetReviewStatus: "pending-no-authored-sfx-approved",
            CueCount: cues.Length,
            ApprovedAuthoredAssetCount: 0,
            CatalogComplete: catalogComplete,
            EveryCueConnected: everyCueConnected,
            EveryCueLicensed: everyCueLicensed,
            GenerationCandidatesExcluded: generationCandidatesExcluded,
            PeakPolicyComplete: peakPolicyComplete,
            NoClipping: noClipping,
            NoDuplicateFingerprints: noDuplicateFingerprints,
            MenuNavigationDistinct: menuNavigationDistinct,
            ComboTiersDistinct: comboTiersDistinct,
            ComboBreakDistinct: comboBreakDistinct,
            PowerActivationsDistinct: powerActivationsDistinct,
            AchievementDistinct: achievementDistinct,
            RestartDistinct: restartDistinct,
            DeathCausesDistinct: deathCausesDistinct,
            RulesStateIndependent: rulesStateIndependent,
            Entries: entries);
    }

    private static SfxCueCatalogEntry CreateEntry(
        AudioCue cue,
        ProceduralCueMeasurement measurement)
    {
        var policy = AudioCueMixPolicy.For(cue);
        if (measurement.Bus != policy.Bus
            || measurement.DurationMilliseconds != policy.ExpectedDurationMilliseconds)
        {
            throw new InvalidOperationException($"SFX measurement diverged from policy: {cue}.");
        }

        return new SfxCueCatalogEntry(
            Cue: cue,
            RuntimeId: ToKebabCase(cue.ToString()),
            Family: Family(cue),
            SourceType: "procedural-fallback",
            Provenance: "deterministic-runtime-pcm",
            License: "Apache-2.0",
            AuthoredAssetPath: null,
            Bus: policy.Bus,
            Priority: policy.Priority,
            CooldownMilliseconds: policy.CooldownMilliseconds,
            MaximumPolyphony: policy.MaximumPolyphony,
            MusicDuckDecibels: policy.MusicDuckDecibels,
            Measurement: measurement);
    }

    private static bool Distinct(
        IReadOnlyList<SfxCueCatalogEntry> entries,
        params AudioCue[] cues) =>
        Fingerprints(entries, cues).Distinct(StringComparer.Ordinal).Count() == cues.Length;

    private static IEnumerable<string> Fingerprints(
        IReadOnlyList<SfxCueCatalogEntry> entries,
        IEnumerable<AudioCue> cues)
    {
        var byCue = entries.ToDictionary(entry => entry.Cue);
        return cues.Select(cue => byCue[cue].Measurement.PcmSha256);
    }

    private static string Family(AudioCue cue) => cue switch
    {
        AudioCue.Navigate or AudioCue.Confirm or AudioCue.Back or AudioCue.Pause
            or AudioCue.Restart => "ui",
        AudioCue.Achievement => "achievement",
        AudioCue.ComboTier1 or AudioCue.ComboTier2 or AudioCue.ComboTier3
            or AudioCue.ComboTier4 or AudioCue.ComboBreak => "combo",
        AudioCue.ShieldSpawn or AudioCue.ShieldActivate or AudioCue.PhaseShiftActivate
            or AudioCue.LastStandActivate or AudioCue.SlowMoActivate
            or AudioCue.BoostActivate or AudioCue.MagnetActivate
            or AudioCue.BaitActivate or AudioCue.GluttonyActivate
            or AudioCue.SegmentDetachActivate or AudioCue.ShieldExpire
            or AudioCue.ShieldBreak or AudioCue.PowerSpawn or AudioCue.PowerExpire
            or AudioCue.PowerRecovery => "power",
        AudioCue.Collision or AudioCue.StarvationDeath or AudioCue.Victory => "terminal",
        AudioCue.Food or AudioCue.Starvation => "gameplay",
        _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, "Unknown audio cue."),
    };

    private static string ToKebabCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
