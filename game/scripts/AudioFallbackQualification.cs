using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record AudioFallbackQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string DriverName,
    string SelectedOutputDevice,
    IReadOnlyList<string> ObservedOutputDevices,
    int CueCount,
    int RapidRetriggerIterations,
    int RapidRetriggerAttempts,
    int MutedPathChecks,
    int SfxBusCapacity,
    int UiBusCapacity,
    int PeakVoiceCount,
    int CooldownSuppressions,
    int PolyphonySuppressions,
    int PrioritySuppressions,
    int Interruptions,
    int MutedSuppressions,
    bool PolicyCatalogComplete,
    bool BusRoutingObserved,
    bool CooldownPolicyObserved,
    bool PolyphonyPolicyObserved,
    bool PriorityPolicyObserved,
    bool InterruptionPolicyObserved,
    bool MusicDuckPolicyObserved,
    bool MusicDuckRestorationObserved,
    bool BusIsolationObserved,
    bool UnitTestableWithoutPlayback,
    bool EngineMusicDuckObserved,
    bool EngineMusicDuckRestored,
    bool SavedVolumesImmediateAndIsolated,
    bool VoiceCapacityBounded,
    bool OutputDevicePollingActive,
    bool DeviceChangeRecoveryObserved,
    bool MissingBusFailureObserved,
    bool BackoffObserved,
    bool RecoveryObserved,
    bool CacheBounded,
    bool CleanupObserved,
    bool RulesStateUnchanged)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
