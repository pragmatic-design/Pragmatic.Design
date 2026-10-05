using Xunit;

namespace Pragmatic.Logging.Tests;

/// <summary>
///     The test classes that redirect <see cref="Console.Out" />. It is process-wide, so two classes
///     redirecting it in parallel would each read the other's output; one collection runs them in turn.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ConsoleOutputCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Console output";
}
