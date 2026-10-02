namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One property of an entity's domain key.
/// </summary>
/// <remarks>
///     A domain key is very often more than one column — a room type is unique per property, a rate is
///     unique per room type and season — so the model holds one entry per part. Everything downstream that names a key column reads this, in declaration order, so the
///     order the developer wrote is the order the index and the lookup use.
/// </remarks>
internal sealed record LogicKeyPart
{
    /// <summary>The property name on the entity.</summary>
    public required string Name { get; init; }

    /// <summary>The property's type, as written.</summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     Whether this part asked for uniqueness across every tenant rather than within one.
    /// </summary>
    /// <remarks>
    ///     Read per part because that is where the attribute sits, and consumed per entity: an index
    ///     has one scope. Parts that disagree are <c>PRAG0625</c> rather than a silent winner — with a
    ///     two-column key and two different answers, whichever one the generator picked would be wrong
    ///     half the time and invisible either way.
    /// </remarks>
    public bool IsGlobal { get; init; }
}
