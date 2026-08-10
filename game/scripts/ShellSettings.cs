using VibeSnake.Persistence;

namespace VibeSnake.Game;

/// <summary>
/// Presentation settings contract for the accessible shell milestone.
/// Maps 1:1 to the current <see cref="PreferencesDocument"/> schema.
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

    public string WindowMode { get; set; } = PreferencesDocument.WindowedMode;

    public string WindowSizePreset { get; set; } = PreferencesDocument.HdWindowSize;

    public bool ReducedMotion { get; set; }

    public bool HighContrast { get; set; }

    public float TextScale { get; set; } = 1.0f;

    public float ScreenShakeIntensity { get; set; } = 1.0f;

    public bool FlashFree { get; set; }

    public float ControllerDeadzone { get; set; } =
        PreferencesDocument.DefaultControllerDeadzone;

    public bool MonoOutput { get; set; }

    public bool VibeAdaptationEnabled { get; set; } = true;

    public bool LocalPlaytestSummariesEnabled { get; set; }

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
            WindowMode = clamped.WindowMode,
            WindowSizePreset = clamped.WindowSizePreset,
            ReducedMotion = clamped.ReducedMotion,
            HighContrast = clamped.HighContrast,
            TextScale = clamped.TextScale,
            ScreenShakeIntensity = clamped.ScreenShakeIntensity,
            FlashFree = clamped.FlashFree,
            ControllerDeadzone = clamped.ControllerDeadzone,
            MonoOutput = clamped.MonoOutput,
            VibeAdaptationEnabled = clamped.VibeAdaptationEnabled,
            LocalPlaytestSummariesEnabled = clamped.LocalPlaytestSummariesEnabled,
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
            FlashFree: FlashFree,
            ControllerDeadzone: ControllerDeadzone,
            MonoOutput: MonoOutput,
            VibeAdaptationEnabled: VibeAdaptationEnabled,
            LocalPlaytestSummariesEnabled: LocalPlaytestSummariesEnabled,
            WindowMode: WindowMode,
            WindowSizePreset: WindowSizePreset).Clamped();

    public void Clamp()
    {
        var clamped = ToDocument();
        MasterVolume = clamped.MasterVolume;
        MusicVolume = clamped.MusicVolume;
        SfxVolume = clamped.SfxVolume;
        UiVolume = clamped.UiVolume;
        TextScale = clamped.TextScale;
        ScreenShakeIntensity = clamped.ScreenShakeIntensity;
        ControllerDeadzone = clamped.ControllerDeadzone;
        Fullscreen = clamped.Fullscreen;
        WindowMode = clamped.WindowMode;
        WindowSizePreset = clamped.WindowSizePreset;
    }

    public float EffectiveMusicVolume() =>
        MasterMuted || MusicMuted ? 0.0f : MasterVolume * MusicVolume;

    public float EffectiveSfxVolume() =>
        MasterMuted || SfxMuted ? 0.0f : MasterVolume * SfxVolume;

    public float EffectiveUiVolume() =>
        MasterMuted || UiMuted ? 0.0f : MasterVolume * UiVolume;

    /// <summary>
    /// Screen-shake scale after accessibility gates. Reduced motion and
    /// flash-free both force zero so later camera effects cannot ignore prefs.
    /// </summary>
    public float EffectiveScreenShakeIntensity() =>
        ReducedMotion || FlashFree ? 0.0f : ScreenShakeIntensity;

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

    /// <summary>
    /// Flips reduced-motion presentation and returns the new state.
    /// Enabling reduced motion also zeroes screen-shake intensity so motion
    /// reduction is not undermined by residual shake preference.
    /// </summary>
    public bool ToggleReducedMotion()
    {
        ReducedMotion = !ReducedMotion;
        if (ReducedMotion)
        {
            ScreenShakeIntensity = 0.0f;
        }

        return ReducedMotion;
    }

    /// <summary>Flips preferred fullscreen mode and returns the new state.</summary>
    public bool ToggleFullscreen()
    {
        WindowMode = WindowMode == PreferencesDocument.WindowedMode
            ? PreferencesDocument.BorderlessMode
            : PreferencesDocument.WindowedMode;
        Fullscreen = WindowMode != PreferencesDocument.WindowedMode;
        return Fullscreen;
    }

    public string CycleWindowMode(int direction)
    {
        string[] modes =
        [
            PreferencesDocument.WindowedMode,
            PreferencesDocument.BorderlessMode,
            PreferencesDocument.ExclusiveFullscreenMode,
        ];
        var current = Array.IndexOf(modes, WindowMode);
        current = current < 0 ? 0 : current;
        WindowMode = modes[(current + Math.Sign(direction) + modes.Length) % modes.Length];
        Fullscreen = WindowMode != PreferencesDocument.WindowedMode;
        return WindowMode;
    }

    public string CycleWindowSizePreset(int direction)
    {
        string[] presets =
        [
            PreferencesDocument.ClassicWindowSize,
            PreferencesDocument.HdWindowSize,
            PreferencesDocument.DesktopWindowSize,
            PreferencesDocument.FullHdWindowSize,
        ];
        var current = Array.IndexOf(presets, WindowSizePreset);
        current = current < 0 ? 0 : current;
        WindowSizePreset = presets[
            (current + Math.Sign(direction) + presets.Length) % presets.Length];
        return WindowSizePreset;
    }

    /// <summary>Default volume step for keyboard accessibility shortcuts.</summary>
    public const float DefaultVolumeStep = 0.05f;

    /// <summary>Default text-scale step for keyboard accessibility shortcuts.</summary>
    public const float DefaultTextScaleStep = 0.05f;

    public const float MinimumTextScale = 0.85f;

    public const float MaximumTextScale = 1.5f;

    public const float MinimumControllerDeadzone =
        PreferencesDocument.MinimumControllerDeadzone;

    public const float MaximumControllerDeadzone =
        PreferencesDocument.MaximumControllerDeadzone;

    public const float DefaultControllerDeadzoneStep = 0.05f;

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

    public float AdjustMusicVolume(float delta) =>
        MusicVolume = AdjustVolume(MusicVolume, delta, unmute: () => MusicMuted = false);

    public float AdjustSfxVolume(float delta) =>
        SfxVolume = AdjustVolume(SfxVolume, delta, unmute: () => SfxMuted = false);

    public float AdjustUiVolume(float delta) =>
        UiVolume = AdjustVolume(UiVolume, delta, unmute: () => UiMuted = false);

    public bool ToggleMusicMute() => MusicMuted = !MusicMuted;

    public bool ToggleSfxMute() => SfxMuted = !SfxMuted;

    public bool ToggleUiMute() => UiMuted = !UiMuted;

    public bool ToggleMonoOutput() => MonoOutput = !MonoOutput;

    public bool ToggleVibeAdaptation() =>
        VibeAdaptationEnabled = !VibeAdaptationEnabled;

    public bool ToggleLocalPlaytestSummaries() =>
        LocalPlaytestSummariesEnabled = !LocalPlaytestSummariesEnabled;

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

    public float AdjustScreenShake(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        ScreenShakeIntensity = Math.Clamp(ScreenShakeIntensity + delta, 0.0f, 1.0f);
        return ScreenShakeIntensity;
    }

    /// <summary>
    /// Adjusts the shared gameplay-stick deadzone. Digital D-pad events do not
    /// use this threshold and therefore remain available at every valid value.
    /// </summary>
    public float AdjustControllerDeadzone(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        ControllerDeadzone = Math.Clamp(
            ControllerDeadzone + delta,
            MinimumControllerDeadzone,
            MaximumControllerDeadzone);
        return ControllerDeadzone;
    }

    public void RestoreControlsDefaults()
    {
        ControllerDeadzone = CreateDefaults().ControllerDeadzone;
    }

    public void RestoreGameplayDefaults()
    {
        VibeAdaptationEnabled = CreateDefaults().VibeAdaptationEnabled;
        LocalPlaytestSummariesEnabled = CreateDefaults().LocalPlaytestSummariesEnabled;
    }

    public void RestoreAudioDefaults()
    {
        var defaults = CreateDefaults();
        MasterVolume = defaults.MasterVolume;
        MusicVolume = defaults.MusicVolume;
        SfxVolume = defaults.SfxVolume;
        UiVolume = defaults.UiVolume;
        MasterMuted = defaults.MasterMuted;
        MusicMuted = defaults.MusicMuted;
        SfxMuted = defaults.SfxMuted;
        UiMuted = defaults.UiMuted;
        MonoOutput = defaults.MonoOutput;
    }

    public void RestoreDisplayDefaults()
    {
        var defaults = CreateDefaults();
        Fullscreen = defaults.Fullscreen;
        WindowMode = defaults.WindowMode;
        WindowSizePreset = defaults.WindowSizePreset;
    }

    public void RestoreAccessibilityDefaults()
    {
        var defaults = CreateDefaults();
        ReducedMotion = defaults.ReducedMotion;
        HighContrast = defaults.HighContrast;
        TextScale = defaults.TextScale;
        ScreenShakeIntensity = defaults.ScreenShakeIntensity;
        FlashFree = defaults.FlashFree;
    }

    private static float AdjustVolume(float value, float delta, Action unmute)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        if (delta > 0.0f)
        {
            unmute();
        }

        return Math.Clamp(value + delta, 0.0f, 1.0f);
    }
}
