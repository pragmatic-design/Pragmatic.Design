namespace Pragmatic.SourceGenerator.Features.Privacy.Models;

/// <summary>
///     How one entity's rows are found from a subject reference: the navigations to follow, and the
///     property on the subject that holds the identity the registry resolves the reference to.
/// </summary>
/// <remarks>
///     <para>
///         <c>[LinksToSubject]</c> carries one hop. Anything further from the subject than a direct
///         child — <c>OrderLine → Order → Customer</c> — is only a path once the hops are composed, and
///         composing them is the difference between an adapter that filters and one that returns
///         everything.
///     </para>
///     <para>
///         Not part of any incremental model: it is computed inside the source-output callback from the
///         entities already collected there, so it never has to be compared for caching.
///     </para>
/// </remarks>
internal sealed class SubjectRoute
{
    /// <summary>
    ///     The navigation properties from the entity to the subject, in order. Empty when the entity
    ///     <em>is</em> the subject.
    /// </summary>
    public required IReadOnlyList<string> Navigations { get; init; }

    /// <summary>The property on the subject that holds the identity.</summary>
    public required string IdentifierProperty { get; init; }

    /// <summary>The declared type of <see cref="IdentifierProperty" />, as written.</summary>
    public required string IdentifierTypeDisplay { get; init; }

    /// <summary>Fully qualified name of the subject type this route ends at.</summary>
    public required string SubjectFullTypeName { get; init; }

    /// <summary>
    ///     How far the entity is from its subject — the number of hops.
    /// </summary>
    /// <remarks>
    ///     Decides the erasure order: the furthest rows go first, because they are the ones holding
    ///     foreign keys into the rows nearer the subject.
    /// </remarks>
    public int Depth => Navigations.Count;

    /// <summary>
    ///     The member access from a row of the entity to the subject's identifier, e.g.
    ///     <c>Order.Customer.Email</c>.
    /// </summary>
    public string IdentifierAccess
        => Navigations.Count == 0
            ? IdentifierProperty
            : string.Join(".", Navigations) + "." + IdentifierProperty;
}
