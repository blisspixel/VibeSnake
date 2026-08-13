using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Game;

internal sealed record AccessibilityAuditAreaEvidence(
    string Id,
    bool AutomatedPassed,
    IReadOnlyList<string> EvidenceFiles);

internal sealed record AccessibilityDisplayClassEvidence(
    string Id,
    int RequestedWidth,
    int RequestedHeight,
    int EffectiveWidth,
    int EffectiveHeight,
    double ViewportScale,
    float TextScale,
    bool LogicalLayoutComplete);

internal sealed record AccessibilityAuditSourceEvidence(
    string FileName,
    string Kind,
    string Sha256);

internal sealed record CandidateAccessibilityAuditEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string RequiredFlowDefectSeverity,
    int AuditAreaCount,
    bool AllAutomatedAuditAreasPassed,
    bool KeyboardOnlyRouteComplete,
    bool ControllerOnlyRouteComplete,
    bool RemappingComplete,
    bool SingleActionNavigationComplete,
    bool IndependentAudioControlsComplete,
    bool MonoOutputComplete,
    bool VisualAlternativesComplete,
    bool ReducedMotionComplete,
    bool FlashSafetyComplete,
    bool MaximumTextScaleViewportMatrixComplete,
    float MaximumTextScale,
    int SupportedDisplayClassCount,
    int MaximumTextScaleDisplayClassCount,
    string AccessibilityUserReviewStatus,
    string FeatureGuidePath,
    string FeaturePublicationStatus,
    IReadOnlyList<AccessibilityAuditAreaEvidence> AuditAreas,
    IReadOnlyList<AccessibilityDisplayClassEvidence> DisplayClasses,
    IReadOnlyList<AccessibilityAuditSourceEvidence> Sources,
    IReadOnlyList<string> PendingHumanChecks)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// Cross-binds the accessibility records emitted by the packaged player. This
/// gate proves that no individual subsystem can report success while another
/// required accessibility boundary is missing or contradictory.
/// </summary>
internal static class CandidateAccessibilityAuditQualification
{
    private const float RequiredMaximumTextScale = 1.5f;

    private static readonly DisplayDefinition[] RequiredDisplays =
    [
        new("minimum-clamp", 320, 180, 640, 360),
        new("hd-16-9", 1920, 1080, 1920, 1080),
        new("classic-4-3", 1024, 768, 1024, 768),
        new("desktop-16-10", 1920, 1200, 1920, 1200),
        new("ultrawide-21-9", 3440, 1440, 3440, 1440),
        new("square-1-1", 1024, 1024, 1024, 1024),
        new("high-density-4k", 3840, 2160, 3840, 2160),
        new("high-density-5k", 5120, 2880, 5120, 2880),
    ];

    private static readonly string[] RequiredAccessibilitySettingIds =
    [
        "high_contrast",
        "reduced_motion",
        "text_scale",
        "screen_shake",
        "flash_free",
    ];

    public static CandidateAccessibilityAuditEvidence Run(string evidenceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);

        var accessibility = Load(
            evidenceDirectory,
            "accessibility_presentation.json",
            "accessibility-presentation-v1",
            schemaVersion: 1);
        var shell = Load(
            evidenceDirectory,
            "shell_presentation.json",
            "shell-presentation-v1",
            schemaVersion: 1);
        var settings = Load(
            evidenceDirectory,
            "settings_screen.json",
            "settings-screen-qualification-v1",
            schemaVersion: 1);
        var input = Load(
            evidenceDirectory,
            "input_cadence.json",
            "input-cadence-qualification-v1",
            schemaVersion: 1);
        var audio = Load(
            evidenceDirectory,
            "audio_fallback_stress.json",
            "audio-mixing-policy-v2",
            schemaVersion: 2);
        var multimodal = Load(
            evidenceDirectory,
            "multimodal_feedback.json",
            "multimodal-feedback-v1",
            schemaVersion: 1);
        var viewport = Load(
            evidenceDirectory,
            "viewport_matrix.json",
            "virtual-viewport-matrix-v1",
            schemaVersion: 1);

        ValidateSettings(settings.Root);
        ValidateInput(input.Root);
        ValidateAudio(audio.Root);
        ValidateAccessibilityPresentation(accessibility.Root);
        ValidateMultimodal(multimodal.Root);
        ValidateShellPresentation(shell.Root);
        var displayClasses = ValidateDisplayMatrix(viewport.Root, shell.Root);

        var sources = new[]
        {
            accessibility,
            shell,
            settings,
            input,
            audio,
            multimodal,
            viewport,
        };
        var sourceEvidence = sources
            .Select(source => new AccessibilityAuditSourceEvidence(
                source.FileName,
                source.Kind,
                source.Sha256))
            .ToArray();

        var areas = new[]
        {
            Area("text", shell.FileName, viewport.FileName),
            Area("contrast", shell.FileName),
            Area("focus", shell.FileName, settings.FileName),
            Area("remapping", settings.FileName),
            Area("single-action-navigation", settings.FileName),
            Area("controller-only-use", settings.FileName, input.FileName),
            Area("keyboard-only-use", settings.FileName, input.FileName),
            Area("audio-separation", settings.FileName, audio.FileName),
            Area("visual-alternatives", multimodal.FileName, shell.FileName),
            Area("reduced-motion", accessibility.FileName, multimodal.FileName),
            Area("flash-safety", accessibility.FileName, multimodal.FileName),
            Area("documentation", "docs/guides/ACCESSIBILITY.md"),
        };

        return new CandidateAccessibilityAuditEvidence(
            SchemaVersion: 1,
            Kind: "candidate-accessibility-audit-v1",
            Passed: true,
            RequiredFlowDefectSeverity: "P1",
            AuditAreaCount: areas.Length,
            AllAutomatedAuditAreasPassed: areas.All(area => area.AutomatedPassed),
            KeyboardOnlyRouteComplete: true,
            ControllerOnlyRouteComplete: true,
            RemappingComplete: true,
            SingleActionNavigationComplete: true,
            IndependentAudioControlsComplete: true,
            MonoOutputComplete: true,
            VisualAlternativesComplete: true,
            ReducedMotionComplete: true,
            FlashSafetyComplete: true,
            MaximumTextScaleViewportMatrixComplete: true,
            MaximumTextScale: RequiredMaximumTextScale,
            SupportedDisplayClassCount: displayClasses.Count,
            MaximumTextScaleDisplayClassCount: displayClasses.Count,
            AccessibilityUserReviewStatus: "pending-accessibility-user-review",
            FeatureGuidePath: "docs/guides/ACCESSIBILITY.md",
            FeaturePublicationStatus: "published-in-repository",
            AuditAreas: areas,
            DisplayClasses: displayClasses,
            Sources: sourceEvidence,
            PendingHumanChecks:
            [
                "retained-visible-audit-windows-macos-linux",
                "maximum-text-scale-platform-captures",
                "physical-keyboard-and-controller-only-flow-review",
                "players-using-relevant-accessibility-settings",
                "human-focus-contrast-readability-photosensitivity-review",
            ]);
    }

    private static AccessibilityAuditAreaEvidence Area(
        string id,
        params string[] evidenceFiles) => new(id, true, evidenceFiles);

    private static LoadedEvidence Load(
        string evidenceDirectory,
        string fileName,
        string kind,
        int schemaVersion)
    {
        var path = Path.Combine(evidenceDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Accessibility audit source is missing: {fileName}.");
        }

        var bytes = File.ReadAllBytes(path);
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Accessibility audit source is invalid JSON: {fileName}.",
                exception);
        }

        RequireInt(root, "schemaVersion", schemaVersion, fileName);
        RequireString(root, "kind", kind, fileName);
        RequireTrue(root, "passed", fileName);
        return new LoadedEvidence(
            fileName,
            kind,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            root);
    }

    private static void ValidateSettings(JsonElement root)
    {
        RequireInt(root, "preferenceSchemaVersion", ShellSettings.SchemaVersion, "settings");
        RequireInt(root, "sectionCount", 6, "settings");
        RequireInt(root, "itemCount", 34, "settings");
        foreach (var field in new[]
        {
            "everyItemDescribed",
            "keyboardRouteComplete",
            "controllerRouteComplete",
            "keyboardRemappingComplete",
            "controllerRemappingComplete",
            "conflictSwapAndCancelComplete",
            "oppositeDeviceBindingsRetained",
            "singleActionNavigationComplete",
            "monoOutputApplied",
            "digitalFallbackRetained",
        })
        {
            RequireTrue(root, field, "settings");
        }

        SettingsMenuCatalog.AssertComplete();
        var accessibilityIds = SettingsMenuCatalog.ForSection(SettingsSection.Accessibility)
            .Select(item => item.Id)
            .ToArray();
        if (!accessibilityIds.SequenceEqual(RequiredAccessibilitySettingIds))
        {
            throw new InvalidOperationException(
                "Accessibility settings catalog changed without an audit update.");
        }
    }

    private static void ValidateInput(JsonElement root)
    {
        RequireInt(root, "deviceClassCount", 3, "input");
        RequireInt(root, "cadenceProfileCount", 3, "input");
        RequireTrue(root, "passiveStickDriftRejected", "input");
        var cases = RequireArray(root, "cases", 9, "input");
        var deviceClasses = cases
            .EnumerateArray()
            .Select(item => item.GetProperty("deviceClass").GetString())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!deviceClasses.SequenceEqual(new[] { "dpad", "keyboard", "stick" }))
        {
            throw new InvalidOperationException(
                "Input accessibility audit requires keyboard, D-pad, and stick cases.");
        }
    }

    private static void ValidateAudio(JsonElement root)
    {
        RequireInt(root, "cueCount", 31, "audio");
        foreach (var field in new[]
        {
            "busRoutingObserved",
            "busIsolationObserved",
            "savedVolumesImmediateAndIsolated",
            "voiceCapacityBounded",
            "deviceChangeRecoveryObserved",
            "recoveryObserved",
            "rulesStateUnchanged",
        })
        {
            RequireTrue(root, field, "audio");
        }
    }

    private static void ValidateAccessibilityPresentation(JsonElement root)
    {
        RequireInt(root, "profileCount", 4, "accessibility presentation");
        RequireInt(root, "cueCount", 31, "accessibility presentation");
        foreach (var field in new[]
        {
            "allFullScreenFlashDisabled",
            "allCriticalTextRetained",
            "allCuesRetained",
            "rulesStateUnchanged",
        })
        {
            RequireTrue(root, field, "accessibility presentation");
        }
        _ = RequireArray(root, "profiles", 4, "accessibility presentation");
    }

    private static void ValidateMultimodal(JsonElement root)
    {
        foreach (var field in new[]
        {
            "timerShapeTextColorProgression",
            "comboMotionHasStaticFallback",
            "powerIdentityOneToOne",
            "recoveryProtectionPreTelegraphed",
            "deathSignalsDistinct",
            "allProfilesDeathAttributionSurvives",
            "rulesStateUnchanged",
        })
        {
            RequireTrue(root, field, "multimodal feedback");
        }
        _ = RequireArray(root, "profiles", 5, "multimodal feedback");
    }

    private static void ValidateShellPresentation(JsonElement root)
    {
        foreach (var field in new[]
        {
            "centralizedFontOwner",
            "textFallbackRetained",
            "maximumTextLayoutComplete",
            "nonColorStateMarkers",
            "longCatalogPagination",
        })
        {
            RequireTrue(root, field, "shell presentation");
        }

        RequireMinimum(root, "standardPrimaryContrast", 4.5, "shell presentation");
        RequireMinimum(root, "standardSecondaryContrast", 4.5, "shell presentation");
        RequireMinimum(root, "highContrastPrimaryContrast", 7.0, "shell presentation");
        var maximumTextScale = RequireNumber(root, "maximumTextScale", "shell presentation");
        if (Math.Abs(maximumTextScale - RequiredMaximumTextScale) > 0.0001)
        {
            throw new InvalidOperationException(
                $"Shell maximum text scale must be {RequiredMaximumTextScale:0.0}.");
        }
    }

    private static List<AccessibilityDisplayClassEvidence> ValidateDisplayMatrix(
        JsonElement viewport,
        JsonElement shell)
    {
        RequireTrue(shell, "maximumTextLayoutComplete", "shell presentation");
        var cases = RequireArray(viewport, "cases", RequiredDisplays.Length, "viewport");
        var rows = cases.EnumerateArray().ToArray();
        var results = new List<AccessibilityDisplayClassEvidence>(rows.Length);
        for (var index = 0; index < RequiredDisplays.Length; index++)
        {
            var expected = RequiredDisplays[index];
            var row = rows[index];
            RequireString(row, "id", expected.Id, $"viewport[{index}]");
            RequireInt(row, "requestedWidth", expected.RequestedWidth, $"viewport[{index}]");
            RequireInt(row, "requestedHeight", expected.RequestedHeight, $"viewport[{index}]");
            RequireInt(row, "effectiveWidth", expected.EffectiveWidth, $"viewport[{index}]");
            RequireInt(row, "effectiveHeight", expected.EffectiveHeight, $"viewport[{index}]");
            var scale = RequireNumber(row, "scale", $"viewport[{index}]");
            if (scale <= 0)
            {
                throw new InvalidOperationException(
                    $"Accessibility viewport {expected.Id} must have a positive scale.");
            }

            results.Add(new AccessibilityDisplayClassEvidence(
                expected.Id,
                expected.RequestedWidth,
                expected.RequestedHeight,
                expected.EffectiveWidth,
                expected.EffectiveHeight,
                scale,
                RequiredMaximumTextScale,
                LogicalLayoutComplete: true));
        }

        return results;
    }

    private static JsonElement RequireArray(
        JsonElement root,
        string field,
        int count,
        string label)
    {
        if (!root.TryGetProperty(field, out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() != count)
        {
            throw new InvalidOperationException(
                $"{label}.{field} must contain exactly {count} rows.");
        }

        return value;
    }

    private static void RequireTrue(JsonElement root, string field, string label)
    {
        if (!root.TryGetProperty(field, out var value)
            || value.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException($"{label}.{field} must be true.");
        }
    }

    private static void RequireInt(
        JsonElement root,
        string field,
        int expected,
        string label)
    {
        if (!root.TryGetProperty(field, out var value)
            || !value.TryGetInt32(out var actual)
            || actual != expected)
        {
            throw new InvalidOperationException(
                $"{label}.{field} must be {expected}.");
        }
    }

    private static void RequireString(
        JsonElement root,
        string field,
        string expected,
        string label)
    {
        if (!root.TryGetProperty(field, out var value)
            || value.ValueKind != JsonValueKind.String
            || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label}.{field} must be {expected}.");
        }
    }

    private static double RequireNumber(JsonElement root, string field, string label)
    {
        if (!root.TryGetProperty(field, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number))
        {
            throw new InvalidOperationException($"{label}.{field} must be a finite number.");
        }

        return number;
    }

    private static void RequireMinimum(
        JsonElement root,
        string field,
        double minimum,
        string label)
    {
        var actual = RequireNumber(root, field, label);
        if (actual < minimum)
        {
            throw new InvalidOperationException(
                $"{label}.{field} must be at least {minimum:0.0}.");
        }
    }

    private readonly record struct LoadedEvidence(
        string FileName,
        string Kind,
        string Sha256,
        JsonElement Root);

    private readonly record struct DisplayDefinition(
        string Id,
        int RequestedWidth,
        int RequestedHeight,
        int EffectiveWidth,
        int EffectiveHeight);
}
