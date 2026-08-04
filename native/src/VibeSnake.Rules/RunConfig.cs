namespace VibeSnake.Rules;

public sealed record RunConfig(
    int Width = 64,
    int Height = 33,
    int StarvationTicks = 600,
    int MaximumDirectionQueue = 3,
    int FoodScore = 10,
    int ComboWindowTicks = 60,
    int SpeedBonusTicks = 30,
    int PowerSpawnIntervalTicks = 300,
    int PowerVisibleTicks = 120,
    int ShieldDurationTicks = 100,
    int PhaseShiftDurationTicks = 100,
    int LastStandRecoveryTicks = 60,
    int SlowMoDurationTicks = 120,
    int BoostDurationTicks = 80,
    int MagnetDurationTicks = 120,
    int GluttonyDurationTicks = 100,
    int SegmentDetachObstacleTicks = 200,
    int SegmentDetachMaxSegments = 5,
    bool EnableNearMiss = false,
    int StarvationWarningTicks = 200)
{
    public const int RulesTickMilliseconds = 50;
    public const int MaximumGridDimension = 4_096;
    public const int MaximumGridCells = 262_144;
    public const int MaximumConfiguredTicks = 1_000_000;
    public const int MaximumDirectionQueueCapacity = 64;
    public const int MaximumFoodScore = 1_000_000;
    public const int MinimumPowerVisibleTicks = 2;
    public const int MinimumShieldDurationTicks = 2;
    public const int MinimumPhaseShiftDurationTicks = 2;
    public const int MinimumLastStandRecoveryTicks = 1;
    public const int MinimumSlowMoDurationTicks = 2;
    public const int MinimumBoostDurationTicks = 2;
    public const int MinimumMagnetDurationTicks = 2;
    public const int MinimumGluttonyDurationTicks = 2;
    public const int MinimumSegmentDetachObstacleTicks = 2;
    public const int MinimumSegmentDetachMaxSegments = 1;
    public const int MaximumSegmentDetachMaxSegments = 64;

    internal void Validate()
    {
        if (Width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "The grid must be at least two cells wide.");
        }

        if (Height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Height), "The grid must be at least two cells high.");
        }

        if (Width > MaximumGridDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Width),
                $"The grid width cannot exceed {MaximumGridDimension} cells.");
        }

        if (Height > MaximumGridDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Height),
                $"The grid height cannot exceed {MaximumGridDimension} cells.");
        }

        if ((long)Width * Height > MaximumGridCells)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Height),
                $"The grid cannot contain more than {MaximumGridCells} cells.");
        }

        if (StarvationTicks <= 0 || StarvationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StarvationTicks),
                $"Starvation cannot exceed {MaximumConfiguredTicks} ticks.");
        }

        // Warning ticks at or above StarvationTicks never fire (short test configs
        // keep the production default of 200 while using smaller starvation budgets).
        if (StarvationWarningTicks < 0 || StarvationWarningTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StarvationWarningTicks),
                $"Starvation warning cannot exceed {MaximumConfiguredTicks} ticks.");
        }

        if (
            MaximumDirectionQueue <= 0
            || MaximumDirectionQueue > MaximumDirectionQueueCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumDirectionQueue),
                $"The direction queue cannot exceed {MaximumDirectionQueueCapacity} entries.");
        }

        if (FoodScore <= 0 || FoodScore > MaximumFoodScore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FoodScore),
                $"The base food score cannot exceed {MaximumFoodScore}.");
        }

        if (ComboWindowTicks <= 0 || ComboWindowTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ComboWindowTicks),
                $"The combo window cannot exceed {MaximumConfiguredTicks} ticks.");
        }

        if (SpeedBonusTicks <= 0 || SpeedBonusTicks > ComboWindowTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedBonusTicks));
        }

        if (PowerSpawnIntervalTicks < 0 || PowerSpawnIntervalTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(PowerSpawnIntervalTicks));
        }

        if (PowerVisibleTicks < MinimumPowerVisibleTicks || PowerVisibleTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(PowerVisibleTicks));
        }

        if (
            ShieldDurationTicks < MinimumShieldDurationTicks
            || ShieldDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(ShieldDurationTicks));
        }

        if (
            PhaseShiftDurationTicks < MinimumPhaseShiftDurationTicks
            || PhaseShiftDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(PhaseShiftDurationTicks));
        }

        if (
            LastStandRecoveryTicks < MinimumLastStandRecoveryTicks
            || LastStandRecoveryTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(LastStandRecoveryTicks));
        }

        if (
            SlowMoDurationTicks < MinimumSlowMoDurationTicks
            || SlowMoDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(SlowMoDurationTicks));
        }

        if (
            BoostDurationTicks < MinimumBoostDurationTicks
            || BoostDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(BoostDurationTicks));
        }

        if (
            MagnetDurationTicks < MinimumMagnetDurationTicks
            || MagnetDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(MagnetDurationTicks));
        }

        if (
            GluttonyDurationTicks < MinimumGluttonyDurationTicks
            || GluttonyDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(GluttonyDurationTicks));
        }

        if (
            SegmentDetachObstacleTicks < MinimumSegmentDetachObstacleTicks
            || SegmentDetachObstacleTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(SegmentDetachObstacleTicks));
        }

        if (
            SegmentDetachMaxSegments < MinimumSegmentDetachMaxSegments
            || SegmentDetachMaxSegments > MaximumSegmentDetachMaxSegments)
        {
            throw new ArgumentOutOfRangeException(nameof(SegmentDetachMaxSegments));
        }
    }
}
