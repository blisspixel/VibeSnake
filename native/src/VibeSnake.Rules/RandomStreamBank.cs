namespace VibeSnake.Rules;

/// <summary>
/// Named random streams for simulation and non-scored presentation.
/// Only the gameplay stream may affect scored rules outcomes.
/// </summary>
public enum RandomStreamKind : byte
{
    Gameplay = 0,
    Ai = 1,
    Cosmetic = 2,
    Radio = 3,
    Copy = 4,
}

/// <summary>
/// Independent PCG32 streams derived from one master seed and fixed sequence IDs.
/// </summary>
public sealed class RandomStreamBank
{
    // Odd sequence IDs keep PCG increments valid for every stream.
    private const ulong GameplaySequence = 54UL;
    private const ulong AiSequence = 97UL;
    private const ulong CosmeticSequence = 141UL;
    private const ulong RadioSequence = 183UL;
    private const ulong CopySequence = 227UL;

    private readonly Pcg32 _gameplay;
    private readonly Pcg32 _ai;
    private readonly Pcg32 _cosmetic;
    private readonly Pcg32 _radio;
    private readonly Pcg32 _copy;

    public RandomStreamBank(ulong masterSeed)
    {
        MasterSeed = masterSeed;
        _gameplay = new Pcg32(masterSeed, GameplaySequence);
        _ai = new Pcg32(masterSeed, AiSequence);
        _cosmetic = new Pcg32(masterSeed, CosmeticSequence);
        _radio = new Pcg32(masterSeed, RadioSequence);
        _copy = new Pcg32(masterSeed, CopySequence);
    }

    public ulong MasterSeed { get; }

    public Pcg32 Gameplay => _gameplay;

    public Pcg32 Ai => _ai;

    public Pcg32 Cosmetic => _cosmetic;

    public Pcg32 Radio => _radio;

    public Pcg32 Copy => _copy;

    public Pcg32 Get(RandomStreamKind kind) => kind switch
    {
        RandomStreamKind.Gameplay => _gameplay,
        RandomStreamKind.Ai => _ai,
        RandomStreamKind.Cosmetic => _cosmetic,
        RandomStreamKind.Radio => _radio,
        RandomStreamKind.Copy => _copy,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown random stream."),
    };

    /// <summary>
    /// Non-gameplay streams must not share the same first draw as gameplay when
    /// advanced independently from the same master seed.
    /// </summary>
    public static bool NonGameplayStreamsAreIndependent(ulong masterSeed)
    {
        var bank = new RandomStreamBank(masterSeed);
        var gameplay = bank.Gameplay.NextUInt();
        var ai = bank.Ai.NextUInt();
        var cosmetic = bank.Cosmetic.NextUInt();
        var radio = bank.Radio.NextUInt();
        var copy = bank.Copy.NextUInt();
        return gameplay != ai
            && gameplay != cosmetic
            && gameplay != radio
            && gameplay != copy;
    }
}
