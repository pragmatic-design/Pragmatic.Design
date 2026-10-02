namespace Pragmatic.Testing.Mocking.SourceGenerator.Models;

/// <summary>An event the mock has to implement for the type to compile.</summary>
internal sealed record MockEventModel
{
    /// <summary>The event name.</summary>
    public required string Name { get; init; }

    /// <summary>The fully-qualified handler type.</summary>
    public required string Type { get; init; }

    /// <summary>The interface that declares it, which qualifies the explicit implementation.</summary>
    public required string DeclaringInterface { get; init; }
}
