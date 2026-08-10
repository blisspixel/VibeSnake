using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record ReplayBrowserQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int BrowserEntryFieldCount,
    IReadOnlyList<double> PlaybackSpeeds,
    bool MetadataComplete,
    bool ExplicitStateBadgesComplete,
    bool RawKeyboardRouteComplete,
    bool RawControllerRouteComplete,
    bool SpeedControlsComplete,
    bool HudToggleComplete,
    bool PauseStepRestartReturnComplete,
    bool AtomicExportComplete,
    bool DeleteConsentComplete,
    bool DeleteCancelLossless,
    bool ConfirmedDeleteExact,
    bool ExportsPreservedAfterDelete,
    bool ProgressionIsolated)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
