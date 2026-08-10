using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record PlayerDataRecoveryQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int CategoryCount,
    bool ExactConfirmationComplete,
    bool CancelWithoutWriteComplete,
    bool BackupBeforeResetComplete,
    bool BackupIntegrityComplete,
    bool SeparateCategoryResetComplete,
    bool CorruptBackupDetected,
    bool CorruptRestoreRejected,
    bool ConflictWithoutOverwriteComplete,
    bool RestoreComplete,
    bool KeyboardRouteComplete,
    bool ControllerRouteComplete,
    bool RecoveryLocationVisible,
    IReadOnlyList<string> Categories)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
