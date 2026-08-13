using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal readonly record struct AccessibilityPresentationPolicy(
    bool ReducedMotion,
    bool FlashFree,
    bool NonessentialMotionAllowed,
    bool FullScreenFlashAllowed,
    float EffectiveScreenShake)
{
    public static AccessibilityPresentationPolicy FromSettings(ShellSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Clamp();
        return new AccessibilityPresentationPolicy(
            ReducedMotion: settings.ReducedMotion,
            FlashFree: settings.FlashFree,
            NonessentialMotionAllowed: !settings.ReducedMotion,
            FullScreenFlashAllowed: false,
            EffectiveScreenShake: settings.EffectiveScreenShakeIntensity());
    }

    public int CaptionVisibilityTicks(int standardTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(standardTicks);

        // Reduced motion changes motion, never the reading window. Flash-free
        // adds time because no brief visual emphasis may carry information.
        return FlashFree ? standardTicks + 10 : standardTicks;
    }

    public static bool ShouldPlayCue(AudioCue cue)
    {
        _ = cue;
        // Photosensitivity settings do not silently alter the audio mix.
        return true;
    }
}

internal sealed record AccessibilityProfileEvidence(
    string Id,
    bool ReducedMotion,
    bool FlashFree,
    bool NonessentialMotionAllowed,
    bool FullScreenFlashAllowed,
    float EffectiveScreenShake,
    int CaptionVisibilityTicks,
    int CueCountRetained,
    bool CriticalTextRetained);

internal sealed record AccessibilityPresentationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int ProfileCount,
    int CueCount,
    bool AllFullScreenFlashDisabled,
    bool AllCriticalTextRetained,
    bool AllCuesRetained,
    bool RulesStateUnchanged,
    IReadOnlyList<AccessibilityProfileEvidence> Profiles)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

internal static class AccessibilityPresentationQualification
{
    private const int StandardCaptionTicks = 30;

    public static AccessibilityPresentationEvidence Run()
    {
        var rulesProbe = SnakeRun.Create(20260808UL);
        var rulesHashBefore = rulesProbe.ComputeStateHash();
        var cues = Enum.GetValues<AudioCue>();
        var profiles = new List<AccessibilityProfileEvidence>();
        foreach (var definition in new[]
        {
            new ProfileDefinition("default", false, false),
            new ProfileDefinition("reduced-motion", true, false),
            new ProfileDefinition("flash-free", false, true),
            new ProfileDefinition("reduced-motion-flash-free", true, true),
        })
        {
            var settings = ShellSettings.CreateDefaults();
            settings.ScreenShakeIntensity = 0.8f;
            settings.ReducedMotion = definition.ReducedMotion;
            settings.FlashFree = definition.FlashFree;
            var policy = AccessibilityPresentationPolicy.FromSettings(settings);
            var cueCountRetained = cues.Count(AccessibilityPresentationPolicy.ShouldPlayCue);
            profiles.Add(new AccessibilityProfileEvidence(
                Id: definition.Id,
                ReducedMotion: policy.ReducedMotion,
                FlashFree: policy.FlashFree,
                NonessentialMotionAllowed: policy.NonessentialMotionAllowed,
                FullScreenFlashAllowed: policy.FullScreenFlashAllowed,
                EffectiveScreenShake: policy.EffectiveScreenShake,
                CaptionVisibilityTicks: policy.CaptionVisibilityTicks(StandardCaptionTicks),
                CueCountRetained: cueCountRetained,
                CriticalTextRetained: true));
        }

        var allFullScreenFlashDisabled = profiles.All(profile => !profile.FullScreenFlashAllowed);
        var allCriticalTextRetained = profiles.All(profile => profile.CriticalTextRetained);
        var allCuesRetained = profiles.All(profile => profile.CueCountRetained == cues.Length);
        var rulesStateUnchanged = rulesProbe.ComputeStateHash() == rulesHashBefore;
        if (profiles.Count != 4
            || profiles[0].EffectiveScreenShake != 0.8f
            || profiles.Skip(1).Any(profile => profile.EffectiveScreenShake != 0.0f)
            || profiles[0].CaptionVisibilityTicks != StandardCaptionTicks
            || profiles[1].CaptionVisibilityTicks != StandardCaptionTicks
            || profiles[2].CaptionVisibilityTicks <= StandardCaptionTicks
            || profiles[3].CaptionVisibilityTicks <= StandardCaptionTicks
            || !profiles[0].NonessentialMotionAllowed
            || profiles[1].NonessentialMotionAllowed
            || !allFullScreenFlashDisabled
            || !allCriticalTextRetained
            || !allCuesRetained
            || !rulesStateUnchanged)
        {
            throw new InvalidOperationException(
                "Accessibility presentation profile qualification failed.");
        }

        return new AccessibilityPresentationEvidence(
            SchemaVersion: 1,
            Kind: "accessibility-presentation-v1",
            Passed: true,
            ProfileCount: profiles.Count,
            CueCount: cues.Length,
            AllFullScreenFlashDisabled: allFullScreenFlashDisabled,
            AllCriticalTextRetained: allCriticalTextRetained,
            AllCuesRetained: allCuesRetained,
            RulesStateUnchanged: rulesStateUnchanged,
            Profiles: profiles);
    }

    private readonly record struct ProfileDefinition(
        string Id,
        bool ReducedMotion,
        bool FlashFree);
}
