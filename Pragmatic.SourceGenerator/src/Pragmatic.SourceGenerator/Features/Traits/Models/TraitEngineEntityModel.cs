using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
///     An entity a trait generates for an engine other than EF Core: a Pragmatic <c>[Entity]</c> declaring its own members, the shape's key among
///     them, rendered by <see cref="Templates.TraitEngineEntityTemplate" />. A trait's child, or the boundary's shared tag.
/// </summary>
/// <param name="Namespace">The parent's namespace, the entity's.</param>
/// <param name="TypeName">The entity (<c>ReviewComment</c>, <c>CatalogTag</c>).</param>
/// <param name="Parent">The parent of a trait's child; null for the shared tag, which belongs to no one entity.</param>
/// <param name="IsSoftDelete">The entity is <c>[SoftDelete]</c>: the base class declares the flag and its stamps.</param>
/// <param name="ConcurrencyAware">
///     The entity is <c>[ConcurrencyAware]</c>, with the shape's <c>RowVersion</c>: a stale write is refused, which is what keeps a counter the
///     actions raise exact without an update by filter (the shared tag's usage count).
/// </param>
/// <param name="Columns">The base class's stored members (<see cref="Transforms.TraitColumns" />).</param>
/// <param name="Unique">The parts of the entity's unique index (<c>[Unique]</c>), empty for none.</param>
/// <param name="References">The navigations to another generated entity, their foreign keys among <paramref name="Columns" />.</param>
/// <param name="Trigger">The trait attribute, for the file's header (<c>[HasComments]</c>).</param>
/// <param name="Rules">The rules nested in the entity it declares with <c>[VisibleWhen]</c> (<see cref="TraitEngineRuleModel" />).</param>
internal sealed record TraitEngineEntityModel(
    string Namespace,
    string TypeName,
    TraitEngineParentModel? Parent,
    bool IsSoftDelete,
    bool ConcurrencyAware,
    EquatableArray<TraitColumnModel> Columns,
    EquatableArray<string> Unique,
    EquatableArray<TraitEngineReferenceModel> References,
    string Trigger,
    EquatableArray<string> Rules);
