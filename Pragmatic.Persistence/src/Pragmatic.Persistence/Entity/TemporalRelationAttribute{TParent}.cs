namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Typed temporal relation with parent scoping.
///     The SG generates ForParent typed query extensions and scopes validation by parent FK.
/// </summary>
/// <typeparam name="TParent">The parent entity type (e.g. Property).</typeparam>
/// <remarks>
///     The SG infers FK property name by convention: {TParent.Name}Id.
///     In addition to Active/ActiveAt/IncludeHistory, this generates
///     For{Parent}(id) and ActiveFor{Parent}(id) typed extensions
///     and parent-scoped validation (MaxActive per parent, not global).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemporalRelationAttribute<TParent> : Attribute
{
    /// <inheritdoc cref="TemporalRelationAttribute.MaxActive"/>
    public int MaxActive { get; set; }

    /// <inheritdoc cref="TemporalRelationAttribute.AllowOverlap"/>
    public bool AllowOverlap { get; set; }
}
