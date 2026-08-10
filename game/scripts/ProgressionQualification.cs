using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record ProgressionQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int ProgressionDocumentSchemaVersion,
    int GoalCount,
    int GoalLaneCount,
    int PacingTierCount,
    int ExactRequirementCount,
    int HighlightedGoalCount,
    bool KeyboardBrowseAndHighlightComplete,
    bool ControllerBrowseAndHighlightComplete,
    bool HighlightRoundTripComplete,
    bool HumanOnlyProgression,
    int RepetitionOnlyGoalCount,
    bool NotificationQueueBounded,
    bool ReducedMotionNotificationReadable,
    int CosmeticSetCount,
    int CosmeticProfileCaseCount,
    bool CosmeticQualificationPassed,
    bool CosmeticRulesIsolationPassed,
    bool CosmeticKeyboardRouteComplete,
    bool CosmeticControllerRouteComplete,
    bool CosmeticSelectionRoundTripComplete,
    int TourSchemaVersion,
    int TourEventCount,
    int TourTierCount,
    bool TourValidationPassed,
    bool PracticeNoncompetitive,
    bool ImmediateRematchAndReplayComplete,
    bool TourKeyboardRouteComplete,
    bool TourControllerRouteComplete,
    bool TourPracticeIsolationComplete,
    bool TourContextReferencesComplete,
    int HumanDistributionCount,
    string HumanDistributionStatus,
    bool AiEvidenceUsedAsHumanTarget)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
