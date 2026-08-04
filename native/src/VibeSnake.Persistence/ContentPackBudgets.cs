namespace VibeSnake.Persistence;

/// <summary>
/// Declared size and timing budgets for core and optional content packs.
/// Actual measurements are recorded against these ceilings during export qualification.
/// </summary>
public static class ContentPackBudgets
{
    public const long CoreCompressedBytesMaximum = 32L * 1024 * 1024;
    public const long CoreInstalledBytesMaximum = 64L * 1024 * 1024;
    public const long CoreWorkingSetBytesMaximum = 128L * 1024 * 1024;
    public const long RadioStationCompressedBytesMaximum = 80L * 1024 * 1024;
    public const long RadioStationInstalledBytesMaximum = 120L * 1024 * 1024;
    public const int CoreInventoryScanMillisecondsMaximum = 2_000;
    public const int CoreColdStartMillisecondsMaximum = 5_000;
    public const string CorePackId = "vibesnake.core";
    public const string RadioPackIdPrefix = "vibesnake.radio.";

    public static bool IsWithinCoreCompressedBudget(long bytes) =>
        bytes >= 0 && bytes <= CoreCompressedBytesMaximum;

    public static bool IsWithinCoreInstalledBudget(long bytes) =>
        bytes >= 0 && bytes <= CoreInstalledBytesMaximum;

    public static bool IsRadioPackId(string packId) =>
        !string.IsNullOrWhiteSpace(packId)
        && packId.StartsWith(RadioPackIdPrefix, StringComparison.Ordinal)
        && packId.Length > RadioPackIdPrefix.Length;
}
