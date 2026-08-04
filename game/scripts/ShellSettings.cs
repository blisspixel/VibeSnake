namespace VibeSnake.Game;

/// <summary>
/// Presentation settings contract for the accessible shell milestone.
/// Values are in-memory defaults until preferences schema 2 lands.
/// </summary>
internal sealed class ShellSettings
{
    public const int SchemaVersion = 2;

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

    public static ShellSettings CreateDefaults() => new();

    public void Clamp()
    {
        MasterVolume = Clamp01(MasterVolume);
        MusicVolume = Clamp01(MusicVolume);
        SfxVolume = Clamp01(SfxVolume);
        UiVolume = Clamp01(UiVolume);
        TextScale = Math.Clamp(TextScale, 0.85f, 1.5f);
        ScreenShakeIntensity = Clamp01(ScreenShakeIntensity);
    }

    public float EffectiveMusicVolume() =>
        MasterMuted || MusicMuted ? 0.0f : MasterVolume * MusicVolume;

    public float EffectiveSfxVolume() =>
        MasterMuted || SfxMuted ? 0.0f : MasterVolume * SfxVolume;

    public float EffectiveUiVolume() =>
        MasterMuted || UiMuted ? 0.0f : MasterVolume * UiVolume;

    private static float Clamp01(float value) => Math.Clamp(value, 0.0f, 1.0f);
}
