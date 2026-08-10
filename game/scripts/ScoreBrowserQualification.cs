using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record ScoreBrowserQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    bool KeyboardOpenComplete,
    bool ControllerOpenComplete,
    bool KeyboardCancelLossless,
    bool ControllerCategoryNavigationComplete,
    bool ExplicitConfirmationRequired,
    bool ControllerImportComplete,
    bool SourceUnchanged,
    bool OneTimeImportComplete,
    bool LegacyCategoryVisible,
    bool LegacyCategoryNoncompetitive,
    bool NativeCategoriesSeparated,
    bool PersonalBestHistoryVisible,
    bool ResetCategoryOwnsScoreHistory,
    int ScoreHistorySchemaVersion,
    int MaximumScoresPerCategory,
    int PersistedFieldsPerScore,
    int ImportedEntryCount,
    string ImportInboxRelativePath,
    string SourceSha256)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
