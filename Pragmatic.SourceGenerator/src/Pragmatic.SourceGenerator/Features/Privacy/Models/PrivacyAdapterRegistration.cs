namespace Pragmatic.SourceGenerator.Features.Privacy.Models;

/// <summary>
///     The two adapter types generated for one entity, as the registration has to name them.
/// </summary>
/// <remarks>
///     The registration cannot resolve these symbols — it runs in the compilation that creates them —
///     so the names travel from whoever rendered the types rather than being looked up. Recomposing
///     them independently is how a registration ends up naming a class that was never emitted.
/// </remarks>
internal sealed class PrivacyAdapterRegistration
{
    /// <summary>The entity the pair covers, for the comment that groups them.</summary>
    public required string EntityFullTypeName { get; init; }

    /// <summary>Fully qualified name of the generated <c>IPersonalDataSource</c>.</summary>
    public required string SourceFqn { get; init; }

    /// <summary>Fully qualified name of the generated <c>IErasureStep</c>.</summary>
    public required string ErasureStepFqn { get; init; }
}
