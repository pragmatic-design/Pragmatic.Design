namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     How <see cref="ServiceTypeDetector" /> classifies a member's type.
/// </summary>
internal enum ServiceTypeClassification
{
    /// <summary>Decidably data: a value the user owns (DTO property, entity value, scalar).</summary>
    Data,

    /// <summary>Decidably a DI dependency: interface, abstract class, DbContext, <c>[Service]</c>, framework type.</summary>
    Service,

    /// <summary>
    ///     A concrete reference type carrying no signal either way — a third-party service
    ///     (<c>HttpClient</c>, <c>NpgsqlConnection</c>) and a user's concrete model class are
    ///     structurally indistinguishable. Callers pick the fallback that is safe for their context and
    ///     say so with a diagnostic rather than guessing silently.
    /// </summary>
    Ambiguous
}
