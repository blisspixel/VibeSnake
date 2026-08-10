using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record OnboardingQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int LessonCount,
    bool TitleFirstComplete,
    bool OptionalOfferComplete,
    bool DirectPlayComplete,
    bool KeyboardRouteComplete,
    bool ControllerRouteComplete,
    bool ActiveDevicePromptsComplete,
    bool SkipPersisted,
    bool CompletionPersisted,
    bool ReplayAvailable,
    bool ResetComplete,
    bool CompetitiveScoreIsolated,
    bool AchievementsIsolated,
    bool ReplaysIsolated,
    IReadOnlyList<string> Lessons)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
