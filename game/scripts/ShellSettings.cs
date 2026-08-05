using VibeSnake.Persistence;

namespace VibeSnake.Game;

/// <summary>
/// Presentation settings contract for the accessible shell milestone.
/// Maps 1:1 to <see cref="PreferencesDocument"/> schema 2.
/// </summary>
internal sealed class ShellSettings
{
    public const int SchemaVersion = PreferencesDocument.CurrentSchemaVersion;

    public float MasterVolume { get; set; } = 0.8f;

    public float MusicVolume { get; set; } = 0.8f;

    public float SfxVolume { get; set; } = 0.8f;

    public float UiVolume { get; set; } = 0.8f;

    public bool MasterMuted { get; set; }

    public bool MusicMuted { get; set; }

    public bool SfxMuted { get; set; }

    public bool UiMuted { get; set; }

    public bool Fullscreen { get; set; }

    public bool ReducedMotion { get; set; }

    public bool HighContrast { get; set; }

    public float TextScale { get; set; } = 1.0f;

    public float ScreenShakeIntensity { get; set; } = 1.0f;

    public bool FlashFree { get; set; }

    public static ShellSettings CreateDefaults() => FromDocument(PreferencesDocument.CreateDefaults());

    public static ShellSettings FromDocument(PreferencesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var clamped = document.Clamped();
        return new ShellSettings
        {
            MasterVolume = clamped.MasterVolume,
            MusicVolume = clamped.MusicVolume,
            SfxVolume = clamped.SfxVolume,
            UiVolume = clamped.UiVolume,
            MasterMuted = clamped.MasterMuted,
            MusicMuted = clamped.MusicMuted,
            SfxMuted = clamped.SfxMuted,
            UiMuted = clamped.UiMuted,
            Fullscreen = clamped.Fullscreen,
            ReducedMotion = clamped.ReducedMotion,
            HighContrast = clamped.HighContrast,
            TextScale = clamped.TextScale,
            ScreenShakeIntensity = clamped.ScreenShakeIntensity,
            FlashFree = clamped.FlashFree,
        };
    }

    public PreferencesDocument ToDocument() =>
        new PreferencesDocument(
            SchemaVersion: SchemaVersion,
            MasterVolume: MasterVolume,
            MusicVolume: MusicVolume,
            SfxVolume: SfxVolume,
            UiVolume: UiVolume,
            MasterMuted: MasterMuted,
            MusicMuted: MusicMuted,
            SfxMuted: SfxMuted,
            UiMuted: UiMuted,
            Fullscreen: Fullscreen,
            ReducedMotion: ReducedMotion,
            HighContrast: HighContrast,
            TextScale: TextScale,
            ScreenShakeIntensity: ScreenShakeIntensity,
            FlashFree: FlashFree).Clamped();

    public void Clamp()
    {
        var clamped = ToDocument();
        MasterVolume = clamped.MasterVolume;
        MusicVolume = clamped.MusicVolume;
        SfxVolume = clamped.SfxVolume;
        UiVolume = clamped.UiVolume;
        TextScale = clamped.TextScale;
        ScreenShakeIntensity = clamped.ScreenShakeIntensity;
    }

    public float EffectiveMusicVolume() =>
        MasterMuted || MusicMuted ? 0.0f : MasterVolume * MusicVolume;

    public float EffectiveSfxVolume() =>
        MasterMuted || SfxMuted ? 0.0f : MasterVolume * SfxVolume;

    public float EffectiveUiVolume() =>
        MasterMuted || UiMuted ? 0.0f : MasterVolume * UiVolume;

    /// <summary>Flips master mute and returns the new muted state.</summary>
    public bool ToggleMasterMute()
    {
        MasterMuted = !MasterMuted;
        return MasterMuted;
    }

    /// <summary>Flips high-contrast presentation and returns the new state.</summary>
    public bool ToggleHighContrast()
    {
        HighContrast = !HighContrast;
        return HighContrast;
    }

    /// <summary>Flips reduced-motion presentation and returns the new state.</summary>
    public bool ToggleReducedMotion()
    {
        ReducedMotion = !ReducedMotion;
        return ReducedMotion;
    }

    /// <summary>Flips preferred fullscreen mode and returns the new state.</summary>
    public bool ToggleFullscreen()
    {
        Fullscreen = !Fullscreen;
        return Fullscreen;
    }

    /// <summary>Default volume step for keyboard accessibility shortcuts.</summary>
    public const float DefaultVolumeStep = 0.05f;

    /// <summary>Default text-scale step for keyboard accessibility shortcuts.</summary>
    public const float DefaultTextScaleStep = 0.05f;

    public const float MinimumTextScale = 0.85f;

    public const float MaximumTextScale = 1.5f;

    /// <summary>
    /// Adjusts master volume by <paramref name="delta"/>, clamps to 0..1, and
    /// returns the new volume. Unmutes master when increasing from muted silence.
    /// </summary>
    public float AdjustMasterVolume(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        MasterVolume = Math.Clamp(MasterVolume + delta, 0.0f, 1.0f);
        if (delta > 0.0f && MasterMuted)
        {
            MasterMuted = false;
        }

        return MasterVolume;
    }

    /// <summary>
    /// Adjusts text scale by <paramref name="delta"/> and clamps to the
    /// preferences schema range (0.85..1.5).
    /// </summary>
    public float AdjustTextScale(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        TextScale = Math.Clamp(TextScale + delta, MinimumTextScale, MaximumTextScale);
        return TextScale;
    }

    /// <summary>Flips flash-free presentation and returns the new state.</summary>
    public bool ToggleFlashFree()
    {
        FlashFree = !FlashFree;
        return FlashFree;
    }
}
