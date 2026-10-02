using Xunit;

namespace Pragmatic.Agent.Tests;

/// <summary>
///     Serializes tests that mutate process-wide environment variables
///     (PRAGMATIC_AGENT_SECRET_KEY, ASPNETCORE_ENVIRONMENT, DOTNET_ENVIRONMENT, PRAGMATIC_AGENT_*).
///     xUnit runs distinct test classes in parallel by default; without this collection two classes
///     could set/clear the same variable concurrently and observe each other's state (flaky).
///     Members of one collection never run in parallel with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EnvironmentSensitiveCollection
{
    public const string Name = "EnvironmentSensitive";
}
