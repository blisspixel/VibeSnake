namespace VibeSnake.Rules.Tests;

/// <summary>
/// Keeps host-dependent throughput evidence isolated from other xUnit
/// collections, including simulation campaigns that use every host core.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RulesThroughputIntegrationGroup
{
    public const string Name = "Rules throughput integration";
}
