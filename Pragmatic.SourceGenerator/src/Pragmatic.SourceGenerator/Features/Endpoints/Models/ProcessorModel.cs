namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a pre/post processor.
/// </summary>
internal sealed record ProcessorModel
{
    /// <summary>
    ///     The fully qualified type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The order in which the processor runs.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    ///     Why the service container cannot build this type, or <c>null</c> when it can.
    /// </summary>
    /// <remarks>
    ///     Carried on the model rather than derived twice: the same fact decides whether the assembly
    ///     registration emits a line for the processor and whether PRAG0534 is reported for it.
    /// </remarks>
    public string? NotConstructibleReason { get; init; }
}
