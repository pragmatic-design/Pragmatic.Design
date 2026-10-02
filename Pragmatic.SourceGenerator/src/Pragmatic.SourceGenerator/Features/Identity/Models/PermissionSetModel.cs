using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>
///     A list of permission values this assembly publishes for the compilations that cannot read it:
///     the member's fully qualified name, and the values it holds.
/// </summary>
internal sealed record PermissionSetModel
{
    /// <summary>The list's fully qualified name — <c>Company.Grants.Shared.Granted</c>.</summary>
    public required string MemberFqn { get; init; }

    /// <summary>The values the list holds, in the order it declares them.</summary>
    public EquatableArray<string> Values { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The constant paths the list names that this run cannot fold — <c>BookingPermissions.Reservation.Read</c>
    ///     and its kind — resolved against the catalogue the same run builds.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Almost every real list is made of these, so they must not be dropped here: a module's own
    ///     permission constants are written by this generator, so nothing binds them while the transform
    ///     runs, and a list of nothing else would come out with no values at all. The feature would then
    ///     report PRAG1016 on the one shape the framework produces, telling the author to write the list
    ///     "as a collection expression" — which it already is. Same reason and the same catalogue as
    ///     <c>WithCataloguedPermissions</c> for a role's own <c>DefaultPermissions</c>.
    /// </remarks>
    public EquatableArray<string> UnresolvedPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The list is written in a shape the generator cannot read at all, so it publishes nothing
    ///     (PRAG1016). A list whose values are all still to be resolved is not this: it carries its
    ///     paths in <see cref="UnresolvedPaths" /> and is judged once the catalogue is known.
    /// </summary>
    /// <remarks>
    ///     Carried rather than dropped in the transform: a marked list that publishes nothing in silence is
    ///     the same failure this attribute exists to end — the reading side would report the role
    ///     (PRAG1015) while the assembly that owns the list said nothing.
    /// </remarks>
    public bool CannotBeRead { get; init; }

    /// <summary>Where the list is declared, for the diagnostic.</summary>
    public LocationInfo? Location { get; init; }
}
