using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Identity.Transforms;

/// <summary>
///     What a role's <c>DefaultPermissions</c> names, as far as the source can tell: the values it could fold,
///     the constant paths it could not (resolved later by the permission catalogue), the <c>[Role]</c> classes
///     it spreads (whose list the generator writes, resolved with them), the spreads it cannot follow at all,
///     and the list it cannot read at all.
/// </summary>
internal sealed record RoleListValues(
    EquatableArray<string> Resolved,
    EquatableArray<string> Unresolved,
    EquatableArray<string> SpreadRoles,
    EquatableArray<string> UnreadableSpreads,
    EquatableArray<string> UnreadableReferences)
{
    public static readonly RoleListValues Empty = new(
        EquatableArray<string>.Empty, EquatableArray<string>.Empty,
        EquatableArray<string>.Empty, EquatableArray<string>.Empty,
        EquatableArray<string>.Empty);

    /// <summary>
    ///     The whole list is a reference this compilation cannot read — a list in another assembly that
    ///     does not publish it (<c>[PermissionSet]</c>).
    /// </summary>
    /// <remarks>
    ///     ⚠️ Distinct from <see cref="Empty" />, which says "this role grants nothing" — a legitimate
    ///     declaration. Answering the two the same way is what let a role be catalogued as granting nothing
    ///     while the runtime granted every entry of the list.
    /// </remarks>
    public static RoleListValues Unreadable(string reference) => Empty with
    {
        UnreadableReferences = new EquatableArray<string>(ImmutableArray.Create(reference))
    };
}
