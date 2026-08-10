using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record LocalPlaytestSummaryQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    int PreferenceSchemaVersion,
    int SummarySchemaVersion,
    string CollectionBasis,
    int RetentionLimit,
    int ExportFileLimit,
    int MaximumDocumentBytes,
    bool DefaultConsentOff,
    bool ConsentKeyboardRouteComplete,
    bool ConsentRoundTrip,
    bool TerminalCaptureHonored,
    bool DisabledCaptureSkipped,
    bool FieldAllowlistExact,
    bool ForbiddenFieldsAbsent,
    bool ExportKeyboardRouteComplete,
    bool DeleteControllerRouteComplete,
    bool DeleteCancelLossless,
    bool StoreAndExportsDeleted,
    bool UploadSurfaceAbsent,
    IReadOnlyList<string> AllowedSummaryFields,
    IReadOnlyList<string> ForbiddenFieldFamilies,
    IReadOnlyList<string> RetentionRules,
    IReadOnlyList<string> Notes)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}
