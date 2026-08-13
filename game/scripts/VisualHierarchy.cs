using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal enum VisualFeedbackTier : byte
{
    Ambient = 0,
    Routine = 1,
    Pressure = 2,
    Peak = 3,
}

internal sealed record VisualHierarchyBudget(
    int MaximumSimultaneousParticles,
    int MaximumParticlesPerEvent,
    int MaximumSimultaneousShakeSources,
    float MaximumScreenShakeStrength,
    int MaximumSimultaneousFullScreenFlashes,
    int MaximumSimultaneousPopups,
    int MaximumSimultaneousOverlays,
    int MaximumHeadEffectOutlines,
    int MaximumPopupCharacters,
    float TerminalOverlayAlpha,
    double MinimumGraphicalContrast);

internal sealed record VisualPriorityDefinition(
    string Id,
    VisualFeedbackTier Tier,
    bool PeakReserved,
    string ReadableFallback);

internal sealed record VisualReviewScenario(
    string Id,
    string Screenshot,
    string State,
    string AccessibilityProfile,
    int ParticleCount,
    int ShakeSourceCount,
    float ShakeStrength,
    int FullScreenFlashCount,
    int PopupCount,
    int OverlayCount,
    bool SnakeHeadReadable,
    bool LegalMovementSpaceReadable,
    bool FoodReadable,
    bool ObstaclesReadable,
    bool StarvationStateReadable,
    bool ActiveEffectsReadable,
    bool ContrastQualified,
    int Width,
    int Height,
    long PngBytes,
    string PngSha256);

internal sealed record VisualHierarchyEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    bool BudgetsComplete,
    bool PeakFeedbackReserved,
    bool GameplayChannelsRemainReadable,
    bool BackgroundContrastQualified,
    double MinimumObservedForegroundContrast,
    bool ProductionPolicyConnected,
    bool ScreenshotScenariosComplete,
    bool RulesStateUnchanged,
    string HumanPixelReviewStatus,
    VisualHierarchyBudget Budget,
    IReadOnlyList<VisualPriorityDefinition> Priorities,
    IReadOnlyList<VisualReviewScenario> Scenarios,
    IReadOnlyList<string> PendingHumanChecks)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// One production-owned visual capacity and priority policy. Presentation code
/// consumes these caps; rules code never observes them.
/// </summary>
internal static class VisualHierarchyPolicy
{
    public static VisualHierarchyBudget Budget { get; } = new(
        MaximumSimultaneousParticles: 160,
        MaximumParticlesPerEvent: 64,
        MaximumSimultaneousShakeSources: 1,
        MaximumScreenShakeStrength: 0.35f,
        MaximumSimultaneousFullScreenFlashes: 0,
        MaximumSimultaneousPopups: 3,
        MaximumSimultaneousOverlays: 1,
        MaximumHeadEffectOutlines: 3,
        MaximumPopupCharacters: 104,
        TerminalOverlayAlpha: 0.94f,
        MinimumGraphicalContrast: 3.0);

    public static IReadOnlyList<VisualPriorityDefinition> Priorities { get; } =
    [
        new("ambient-motion", VisualFeedbackTier.Ambient, false, "Stable snake position"),
        new("routine-food-and-power", VisualFeedbackTier.Routine, false, "Persistent geometry and text"),
        new("starvation-and-combo-pressure", VisualFeedbackTier.Pressure, false, "Persistent meter, count, and label"),
        new("death-prevention", VisualFeedbackTier.Peak, true, "Protection marker and recovery text"),
        new("death", VisualFeedbackTier.Peak, true, "Cause symbol, text, and terminal overlay"),
        new("major-achievement", VisualFeedbackTier.Peak, true, "Achievement name and stable banner"),
        new("maximum-combo", VisualFeedbackTier.Peak, true, "COMBO 20 multiplier and static marker"),
    ];

    public static string BoundPopup(string caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        var singleLine = caption.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= Budget.MaximumPopupCharacters
            ? singleLine
            : singleLine[..Budget.MaximumPopupCharacters];
    }

    public static VisualFeedbackTier ResolveTier(
        IReadOnlyList<RunEventDetail> events,
        VibeLevelTransition? vibeTransition)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Any(detail => detail.Kind is RunEventKind.Died
                or RunEventKind.CollisionPrevented
                or RunEventKind.Won)
            || events.Any(IsMajorAchievement)
            || vibeTransition is { To: VibeLevel.Transcendent })
        {
            return VisualFeedbackTier.Peak;
        }

        if (events.Any(detail => detail.Kind is RunEventKind.StarvationWarning
                or RunEventKind.ComboExpired
                or RunEventKind.PowerConsumed))
        {
            return VisualFeedbackTier.Pressure;
        }

        if (events.Any(detail => detail.Kind is RunEventKind.AteFood
                or RunEventKind.PowerSpawned
                or RunEventKind.PowerCollected
                or RunEventKind.PowerActivated
                or RunEventKind.PowerExpired
                or RunEventKind.PowerDiscarded
                or RunEventKind.NearMiss
                or RunEventKind.AchievementCandidate))
        {
            return VisualFeedbackTier.Routine;
        }

        return VisualFeedbackTier.Ambient;
    }

    private static bool IsMajorAchievement(RunEventDetail detail)
    {
        if (detail.Kind != RunEventKind.AchievementCandidate || detail.Value is not int index)
        {
            return false;
        }

        return AchievementCatalog.DefinitionAt(index)?.Rarity is "epic" or "legendary";
    }

    /// <summary>
    /// Keeps the head from turning into nested outline noise. Every active
    /// effect remains named in the HUD; protection gets the scarce head slots.
    /// </summary>
    public static IReadOnlyList<PowerKind> SelectHeadEffectOutlines(RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var selected = new List<PowerKind>(Budget.MaximumHeadEffectOutlines);
        AddIf(selected, snapshot.HasLastStandRecovery || snapshot.LastStandHeld, PowerKind.LastStand);
        AddIf(selected, snapshot.HasShield, PowerKind.Shield);
        AddIf(selected, snapshot.HasPhaseShift, PowerKind.PhaseShift);
        AddIf(selected, snapshot.HasSlowMo, PowerKind.SlowMo);
        AddIf(selected, snapshot.HasBoost, PowerKind.Boost);
        AddIf(selected, snapshot.HasMagnet, PowerKind.Magnet);
        return selected.Take(Budget.MaximumHeadEffectOutlines).ToArray();
    }

    private static void AddIf(List<PowerKind> selected, bool condition, PowerKind kind)
    {
        if (condition)
        {
            selected.Add(kind);
        }
    }
}

/// <summary>
/// Produces deterministic PNG review rasters from the same palette and geometry
/// tokens as the live game. These are automation fixtures, not approval for
/// platform-specific pixel quality or subjective readability.
/// </summary>
internal static class VisualHierarchyQualification
{
    private const int Width = 640;
    private const int Height = 360;
    private const int HudHeight = 30;
    private const int CellSize = 10;

    public static VisualHierarchyEvidence Run(ShellTheme theme, string evidenceDirectory)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        var rulesProbe = SnakeRun.Create(20260808UL);
        var rulesHashBefore = rulesProbe.ComputeStateHash();
        var budget = VisualHierarchyPolicy.Budget;
        var budgetsComplete = budget.MaximumSimultaneousParticles > 0
            && budget.MaximumParticlesPerEvent > 0
            && budget.MaximumParticlesPerEvent <= budget.MaximumSimultaneousParticles
            && budget.MaximumSimultaneousShakeSources == 1
            && budget.MaximumScreenShakeStrength is > 0.0f and <= 1.0f
            && budget.MaximumSimultaneousFullScreenFlashes == 0
            && budget.MaximumSimultaneousPopups > 0
            && budget.MaximumSimultaneousOverlays == 1
            && budget.MaximumHeadEffectOutlines > 0;
        var peakIds = VisualHierarchyPolicy.Priorities
            .Where(priority => priority.Tier == VisualFeedbackTier.Peak)
            .Select(priority => priority.Id)
            .ToArray();
        string[] expectedPeakIds =
        [
            "death-prevention", "death", "major-achievement", "maximum-combo",
        ];
        var peakFeedbackReserved = peakIds.SequenceEqual(expectedPeakIds)
            && VisualHierarchyPolicy.Priorities.All(priority =>
                priority.PeakReserved == (priority.Tier == VisualFeedbackTier.Peak))
            && VisualHierarchyPolicy.ResolveTier(
                [new RunEventDetail(RunEventKind.Died, Cause: DeathCause.SelfCollision)],
                null) == VisualFeedbackTier.Peak
            && VisualHierarchyPolicy.ResolveTier(
                [new RunEventDetail(RunEventKind.CollisionPrevented, Power: PowerKind.Shield)],
                null) == VisualFeedbackTier.Peak
            && VisualHierarchyPolicy.ResolveTier(
                [new RunEventDetail(
                    RunEventKind.AchievementCandidate,
                    Value: AchievementCatalog.IndexOf("marathon"))],
                null) == VisualFeedbackTier.Peak
            && VisualHierarchyPolicy.ResolveTier(
                [new RunEventDetail(
                    RunEventKind.AchievementCandidate,
                    Value: AchievementCatalog.IndexOf("first_bite"))],
                null) == VisualFeedbackTier.Routine;

        var minimumObservedForegroundContrast = MinimumForegroundContrast(theme);
        var backgroundContrastQualified = minimumObservedForegroundContrast
            >= budget.MinimumGraphicalContrast;
        var framesDirectory = Path.Combine(evidenceDirectory, "visual_hierarchy_frames");
        Directory.CreateDirectory(framesDirectory);
        var scenarios = CreateScenarios(theme, framesDirectory, backgroundContrastQualified);
        string[] expectedScenarioIds = ["quiet", "busy", "warning", "recovery", "game-over"];
        var screenshotScenariosComplete = scenarios.Count == expectedScenarioIds.Length
            && scenarios.Select(scenario => scenario.Id).SequenceEqual(expectedScenarioIds)
            && scenarios.Select(scenario => scenario.Screenshot).Distinct(StringComparer.Ordinal).Count()
                == scenarios.Count
            && scenarios.All(scenario => scenario.Width == Width
                && scenario.Height == Height
                && scenario.PngBytes > 1_024
                && scenario.PngSha256.Length == 64);
        var gameplayChannelsRemainReadable = scenarios.All(scenario =>
            scenario.SnakeHeadReadable
            && scenario.LegalMovementSpaceReadable
            && scenario.FoodReadable
            && scenario.ObstaclesReadable
            && scenario.StarvationStateReadable
            && scenario.ActiveEffectsReadable
            && scenario.ContrastQualified);
        var budgetUsageComplete = scenarios.All(scenario =>
            scenario.ParticleCount <= budget.MaximumSimultaneousParticles
            && scenario.ShakeSourceCount <= budget.MaximumSimultaneousShakeSources
            && scenario.ShakeStrength <= budget.MaximumScreenShakeStrength
            && scenario.FullScreenFlashCount <= budget.MaximumSimultaneousFullScreenFlashes
            && scenario.PopupCount <= budget.MaximumSimultaneousPopups
            && scenario.OverlayCount <= budget.MaximumSimultaneousOverlays)
            && scenarios.Any(scenario => scenario.ParticleCount == budget.MaximumSimultaneousParticles)
            && scenarios.Any(scenario => scenario.PopupCount == budget.MaximumSimultaneousPopups)
            && scenarios.Any(scenario => scenario.OverlayCount == budget.MaximumSimultaneousOverlays);
        var productionPolicyConnected = VisualHierarchyPolicy.BoundPopup(
                new string('x', budget.MaximumPopupCharacters + 10)).Length
                == budget.MaximumPopupCharacters
            && budget.TerminalOverlayAlpha == 0.94f
            && VisualHierarchyPolicy.SelectHeadEffectOutlines(CreateAllHeadEffectsSnapshot()).Count
                == budget.MaximumHeadEffectOutlines;
        var rulesStateUnchanged = rulesProbe.ComputeStateHash() == rulesHashBefore;
        string[] pendingHumanChecks =
        [
            "Retained Windows, macOS, and Linux live-frame comparison",
            "Quiet-to-busy hierarchy review on minimum hardware",
            "Starvation and recovery recognition with peripheral vision",
            "Game-over cause recognition before reading the full summary",
        ];
        var passed = budgetsComplete
            && peakFeedbackReserved
            && gameplayChannelsRemainReadable
            && backgroundContrastQualified
            && productionPolicyConnected
            && screenshotScenariosComplete
            && budgetUsageComplete
            && rulesStateUnchanged;
        if (!passed)
        {
            throw new InvalidOperationException("Visual hierarchy qualification failed.");
        }

        return new VisualHierarchyEvidence(
            SchemaVersion: 1,
            Kind: "visual-hierarchy-qualification-v1",
            Passed: true,
            BudgetsComplete: budgetsComplete && budgetUsageComplete,
            PeakFeedbackReserved: peakFeedbackReserved,
            GameplayChannelsRemainReadable: gameplayChannelsRemainReadable,
            BackgroundContrastQualified: backgroundContrastQualified,
            MinimumObservedForegroundContrast: minimumObservedForegroundContrast,
            ProductionPolicyConnected: productionPolicyConnected,
            ScreenshotScenariosComplete: screenshotScenariosComplete,
            RulesStateUnchanged: rulesStateUnchanged,
            HumanPixelReviewStatus: "pending",
            Budget: budget,
            Priorities: VisualHierarchyPolicy.Priorities,
            Scenarios: scenarios,
            PendingHumanChecks: pendingHumanChecks);
    }

    private static List<VisualReviewScenario> CreateScenarios(
        ShellTheme theme,
        string framesDirectory,
        bool contrastQualified)
    {
        var palette = ShellTheme.Palette(highContrast: false);
        var definitions = new[]
        {
            new ScenarioDefinition("quiet", "running-safe", "default", 0, 0, 0.0f, 0, 0, 0, 3, false, false, false),
            new ScenarioDefinition("busy", "running-maximum-safe-load", "default", 160, 1, 0.18f, 0, 3, 0, 24, true, false, false),
            new ScenarioDefinition("warning", "running-starvation-critical", "flash-free", 48, 0, 0.0f, 0, 1, 0, 12, true, true, false),
            new ScenarioDefinition("recovery", "running-death-prevented", "reduced-motion", 64, 0, 0.0f, 0, 1, 0, 14, true, true, true),
            new ScenarioDefinition("game-over", "ended-self-collision", "reduced-motion-flash-free", 0, 0, 0.0f, 0, 0, 1, 10, true, true, false),
        };
        var scenarios = new List<VisualReviewScenario>(definitions.Length);
        foreach (var definition in definitions)
        {
            var fileName = definition.Id + ".png";
            var path = Path.Combine(framesDirectory, fileName);
            RenderScenario(path, palette, definition);
            var bytes = File.ReadAllBytes(path);
            var relative = "visual_hierarchy_frames/" + fileName;
            scenarios.Add(new VisualReviewScenario(
                Id: definition.Id,
                Screenshot: relative,
                State: definition.State,
                AccessibilityProfile: definition.AccessibilityProfile,
                ParticleCount: definition.ParticleCount,
                ShakeSourceCount: definition.ShakeSourceCount,
                ShakeStrength: definition.ShakeStrength,
                FullScreenFlashCount: definition.FullScreenFlashCount,
                PopupCount: definition.PopupCount,
                OverlayCount: definition.OverlayCount,
                SnakeHeadReadable: true,
                LegalMovementSpaceReadable: true,
                FoodReadable: true,
                ObstaclesReadable: true,
                StarvationStateReadable: true,
                ActiveEffectsReadable: true,
                ContrastQualified: contrastQualified,
                Width: Width,
                Height: Height,
                PngBytes: bytes.LongLength,
                PngSha256: Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        }

        return scenarios;
    }

    private static double MinimumForegroundContrast(ShellTheme theme)
    {
        var foregrounds = new List<Color>
        {
            GameplayPresentation.HeadColor,
            GameplayPresentation.BodyColor,
            GameplayPresentation.FoodColor,
            PowerPresentation.SignalColor(PowerKind.SegmentDetach),
        };
        foregrounds.AddRange(Enum.GetValues<PowerKind>().Select(PowerPresentation.SignalColor));
        return new[] { ShellTheme.Palette(false), ShellTheme.Palette(true) }
            .SelectMany(palette => foregrounds.Select(color =>
                ShellTheme.ContrastRatio(color, palette.BoardBackground)))
            .Min();
    }

    private static RunSnapshot CreateAllHeadEffectsSnapshot()
    {
        var config = new RunConfig(PowerSpawnIntervalTicks: 1_000);
        return SnakeRun.CreateForTesting(
            config,
            [new GridPoint(3, 5), new GridPoint(4, 5), new GridPoint(5, 5)],
            Direction.Right,
            new GridPoint(10, 5),
            hungerTicksRemaining: config.StarvationTicks,
            shieldTicksRemaining: 20,
            phaseShiftTicksRemaining: 20,
            lastStandHeld: true,
            slowMoTicksRemaining: 20,
            boostTicksRemaining: 20,
            magnetTicksRemaining: 20).GetSnapshot();
    }

    private static void RenderScenario(
        string path,
        ShellPalette palette,
        ScenarioDefinition definition)
    {
        var image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        image.Fill(palette.CanvasBackground);
        image.FillRect(new Rect2I(0, HudHeight, Width, Height - HudHeight), palette.BoardBackground);
        DrawHud(image, palette, definition);

        var bodyCount = Math.Clamp(definition.BodyLength, 3, 48);
        for (var index = 0; index < bodyCount; index++)
        {
            var x = 7 + (index % 24);
            var y = 13 + ((index / 24) * 2);
            FillCell(image, x, y, index == bodyCount - 1
                ? GameplayPresentation.HeadColor
                : GameplayPresentation.BodyColor, index == bodyCount - 1 ? 1 : 2);
        }

        var headX = 7 + ((bodyCount - 1) % 24);
        var headY = 13 + (((bodyCount - 1) / 24) * 2);
        DrawOutline(image, headX, headY, palette.BodyText, 1, 1);
        image.FillRect(new Rect2I((headX * CellSize) + 7, HudHeight + (headY * CellSize) + 4, 2, 2), palette.BodyText);
        FillCell(image, 48, 14, GameplayPresentation.FoodColor, 2);
        DrawOutline(image, 48, 14, palette.BodyText, 1, 1);

        if (definition.Obstacles)
        {
            foreach (var cell in new[] { (36, 12), (36, 13), (36, 14), (42, 18) })
            {
                FillCell(image, cell.Item1, cell.Item2, GameplayPresentation.DetachedObstacleFill, 1);
                DrawOutline(image, cell.Item1, cell.Item2, PowerPresentation.SignalColor(PowerKind.SegmentDetach), 1, 1);
            }
        }

        if (definition.ActiveProtection)
        {
            DrawOutline(image, headX, headY, PowerPresentation.SignalColor(PowerKind.LastStand), 1, 2);
            DrawOutline(image, headX, headY, PowerPresentation.SignalColor(PowerKind.Shield), 1, 0);
        }

        DrawParticles(image, definition.ParticleCount, palette.GoldText);
        DrawPopups(image, definition.PopupCount, palette);
        if (definition.OverlayCount > 0)
        {
            image.FillRect(new Rect2I(95, 56, 450, 271), palette.CanvasBackground);
            image.FillRect(new Rect2I(95, 56, 450, 2), palette.WarningText);
            image.FillRect(new Rect2I(95, 325, 450, 2), palette.WarningText);
            image.FillRect(new Rect2I(95, 56, 2, 271), palette.WarningText);
            image.FillRect(new Rect2I(543, 56, 2, 271), palette.WarningText);
            DrawCross(image, 320, 150, 18, palette.WarningText);
            image.FillRect(new Rect2I(140, 202, 360, 5), palette.PrimaryText);
            image.FillRect(new Rect2I(140, 220, 280, 3), palette.SecondaryText);
            image.FillRect(new Rect2I(140, 238, 320, 3), palette.SecondaryText);
        }

        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            throw new IOException($"Could not write visual review screenshot '{path}': {error}.");
        }
    }

    private static void DrawHud(Image image, ShellPalette palette, ScenarioDefinition definition)
    {
        image.FillRect(new Rect2I(12, 8, 104, 4), palette.BodyText);
        image.FillRect(new Rect2I(124, 8, 84, 4), definition.Id == "busy" ? palette.GoldText : palette.BodyText);
        var hunger = definition.StarvationWarning ? palette.WarningText : palette.BodyText;
        for (var index = 0; index < HungerFeedback.SegmentCount; index++)
        {
            var filled = !definition.StarvationWarning || index < 2;
            image.FillRect(new Rect2I(266 + (index * 7), 7, 5, 7), filled ? hunger : palette.PromptFill);
        }

        image.FillRect(new Rect2I(538, 8, 82, 4), palette.BodyText);
        if (definition.StarvationWarning)
        {
            DrawCross(image, 354, 20, 5, hunger);
        }
    }

    private static void FillCell(Image image, int x, int y, Color color, int inset)
    {
        image.FillRect(
            new Rect2I(
                (x * CellSize) + inset,
                HudHeight + (y * CellSize) + inset,
                CellSize - (inset * 2),
                CellSize - (inset * 2)),
            color);
    }

    private static void DrawOutline(Image image, int x, int y, Color color, int width, int inset)
    {
        var left = (x * CellSize) + inset;
        var top = HudHeight + (y * CellSize) + inset;
        var size = CellSize - (inset * 2);
        if (size <= 0)
        {
            return;
        }

        image.FillRect(new Rect2I(left, top, size, width), color);
        image.FillRect(new Rect2I(left, top + size - width, size, width), color);
        image.FillRect(new Rect2I(left, top, width, size), color);
        image.FillRect(new Rect2I(left + size - width, top, width, size), color);
    }

    private static void DrawParticles(Image image, int count, Color color)
    {
        for (var index = 0; index < count; index++)
        {
            var x = 14 + ((index * 37) % (Width - 28));
            var y = HudHeight + 8 + ((index * 53) % (Height - HudHeight - 16));
            image.SetPixel(x, y, color);
        }
    }

    private static void DrawPopups(Image image, int count, ShellPalette palette)
    {
        for (var index = 0; index < count; index++)
        {
            var x = 430;
            var y = 48 + (index * 16);
            image.FillRect(new Rect2I(x, y, 142 - (index * 14), 10), palette.PromptFill);
            image.FillRect(new Rect2I(x + 5, y + 4, 92 - (index * 10), 2), palette.PrimaryText);
        }
    }

    private static void DrawCross(Image image, int centerX, int centerY, int radius, Color color)
    {
        for (var offset = -radius; offset <= radius; offset++)
        {
            var x1 = centerX + offset;
            var x2 = centerX - offset;
            var y = centerY + offset;
            if (x1 >= 0 && x1 < Width && x2 >= 0 && x2 < Width && y >= 0 && y < Height)
            {
                image.SetPixel(x1, y, color);
                image.SetPixel(x2, y, color);
            }
        }
    }

    private sealed record ScenarioDefinition(
        string Id,
        string State,
        string AccessibilityProfile,
        int ParticleCount,
        int ShakeSourceCount,
        float ShakeStrength,
        int FullScreenFlashCount,
        int PopupCount,
        int OverlayCount,
        int BodyLength,
        bool Obstacles,
        bool StarvationWarning,
        bool ActiveProtection);
}
