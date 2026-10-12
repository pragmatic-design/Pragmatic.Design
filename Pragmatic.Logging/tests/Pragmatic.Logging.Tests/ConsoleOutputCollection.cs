using Xunit;

namespace Pragmatic.Logging.Tests;

/// <summary>
///     The test classes that redirect <see cref="Console.Out" /> or add a listener to
///     <see cref="System.Diagnostics.Trace.Listeners" />. Both are process-wide, so two classes using
///     either in parallel would each read the other's output; one collection runs them in turn.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ConsoleOutputCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Console output";
}
