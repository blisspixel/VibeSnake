namespace VibeSnake.Rules;

public enum RunStatus : byte
{
    Running = 0,
    Dead = 1,
    Won = 2,
}

public enum DeathCause : byte
{
    None = 0,
    SelfCollision = 1,
    Starvation = 2,
}

[Flags]
public enum RunEvent : ushort
{
    None = 0,
    Moved = 1,
    AteFood = 2,
    Wrapped = 4,
    Died = 8,
    Won = 16,
    PowerSpawned = 32,
    PowerCollected = 64,
    PowerActivated = 128,
    PowerExpired = 256,
    PowerConsumed = 512,
    PowerDiscarded = 1024,
    CollisionPrevented = 2048,
    NearMiss = 4096,
}

public enum RunEventKind : byte
{
    DirectionChanged = 0,
    Moved = 1,
    Wrapped = 2,
    AteFood = 3,
    ScoreChanged = 4,
    HungerReset = 5,
    Died = 6,
    Won = 7,
    PowerSpawned = 8,
    PowerCollected = 9,
    PowerActivated = 10,
    PowerExpired = 11,
    PowerConsumed = 12,
    PowerDiscarded = 13,
    CollisionPrevented = 14,
    NearMiss = 15,
}

public readonly record struct RunEventDetail(
    RunEventKind Kind,
    GridPoint? Position = null,
    Direction? NewDirection = null,
    int? Value = null,
    DeathCause? Cause = null,
    PowerKind? Power = null);
