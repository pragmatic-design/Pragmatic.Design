namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Typed temporal relation expressing "between TParent and TChild".
///     The SG generates ForParent/ForChild typed query extensions and scopes validation by parent FK.
/// </summary>
/// <typeparam name="TParent">The parent entity type (e.g. Property).</typeparam>
/// <typeparam name="TChild">The child entity type (e.g. Staff).</typeparam>
/// <remarks>
///     The SG infers FK property names by convention: {TParent.Name}Id and {TChild.Name}Id.
///     In addition to Active/ActiveAt/IncludeHistory, this generates
///     For{Parent}/For{Child} typed extensions and parent-scoped validation.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemporalRelationAttribute<TParent, TChild> : Attribute
{
    /// <inheritdoc cref="TemporalRelationAttribute.MaxActive"/>
    public int MaxActive { get; set; }

    /// <inheritdoc cref="TemporalRelationAttribute.AllowOverlap"/>
    public bool AllowOverlap { get; set; }
}
