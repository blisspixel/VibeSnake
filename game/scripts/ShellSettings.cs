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
}
