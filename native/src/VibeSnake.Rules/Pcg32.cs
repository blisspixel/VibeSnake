using System.Numerics;

namespace VibeSnake.Rules;

public sealed class Pcg32
{
    public const string AlgorithmId = "pcg-xsh-rr-32-v1";

    private const ulong Multiplier = 6364136223846793005UL;
    private ulong _state;
    private readonly ulong _increment;

    public Pcg32(ulong seed, ulong sequence = 54UL)
    {
        _increment = unchecked((sequence << 1) | 1UL);
        _state = 0UL;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    internal Pcg32(ulong state, ulong increment, bool restoreState)
    {
        if (!restoreState)
        {
            throw new ArgumentException("The internal constructor is only for restoring state.", nameof(restoreState));
        }

        if ((increment & 1UL) == 0UL)
        {
            throw new ArgumentException("PCG increment must be odd.", nameof(increment));
        }

        _state = state;
        _increment = increment;
    }

    public ulong State => _state;

    public ulong Increment => _increment;

    public uint NextUInt()
    {
        var oldState = _state;
        _state = unchecked((oldState * Multiplier) + _increment);
        var xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        var rotation = (int)(oldState >> 59);
        return BitOperations.RotateRight(xorShifted, rotation);
    }

    public int NextInt(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        }

        var bound = (uint)exclusiveUpperBound;
        var threshold = unchecked(0U - bound) % bound;

        while (true)
        {
            var value = NextUInt();
            if (value >= threshold)
            {
                return (int)(value % bound);
            }
        }
    }
}
