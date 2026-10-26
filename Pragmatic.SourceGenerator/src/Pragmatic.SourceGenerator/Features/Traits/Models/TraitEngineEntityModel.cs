using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
///     A trait's child as an engine other than EF Core persists it: a Pragmatic <c>[Entity]</c> declaring its own members, the shape's key among
///     them, rendered by <see cref="Templates.TraitEngineEntityTemplate" />.
/// </summary>
/// <param name="Namespace">The parent's namespace, the child's.</param>
/// <param name="TypeName">The child (<c>ReviewComment</c>).</param>
/// <param name="ParentTypeName">The parent, which names the reference navigation too.</param>
/// <param name="ParentFullTypeName">The parent, fully qualified without <c>global::</c>.</param>
/// <param name="ParentForeignKey">The foreign key to the parent (<c>ReviewId</c>).</param>
/// <param name="ParentKeyType">The parent's key type, fully qualified without <c>global::</c> (<c>System.Guid</c>).</param>
/// <param name="IsSoftDelete">The child is <c>[SoftDelete]</c>: the base class declares the flag and its stamps.</param>
/// <param name="Columns">The base class's stored members (<see cref="Transforms.TraitColumns" />).</param>
/// <param name="Trigger">The trait attribute, for the file's header (<c>[HasComments]</c>).</param>
/// <param name="Rules">The rules nested in the child it declares with <c>[VisibleWhen]</c> (<see cref="TraitEngineRuleModel" />).</param>
internal sealed record TraitEngineEntityModel(
    string Namespace,
    string TypeName,
    string ParentTypeName,
    string ParentFullTypeName,
    string ParentForeignKey,
    string ParentKeyType,
    bool IsSoftDelete,
    EquatableArray<TraitColumnModel> Columns,
    string Trigger,
    EquatableArray<string> Rules);
