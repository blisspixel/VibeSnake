using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record RunEndQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    bool SummaryOrderComplete,
    bool CollisionAttributionComplete,
    bool StarvationAttributionComplete,
    bool RecoveryHintComplete,
    bool PersonalBestPersisted,
    bool FairCategorySeparated,
    bool SameInputRestartRejected,
    bool LaterIntentAccepted,
    bool OnlyConfirmRestarts,
    bool KeyboardRestartComplete,
    bool ControllerRestartComplete,
    bool MenuAccessRetained,
    bool SettingsAccessRetained,
    bool ReplayAccessRetained,
    bool UnlockSummaryComplete)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
