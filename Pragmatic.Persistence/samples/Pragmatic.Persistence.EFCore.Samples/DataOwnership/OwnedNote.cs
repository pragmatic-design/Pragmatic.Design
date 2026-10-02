using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.DataOwnership;

/// <summary>
///     L1 — mirrors what <c>[HasOwner]</c> gets you in host mode.
///
///     In a composition host the SG expands <c>[Entity] [HasOwner]</c>
///     into a partial class that: (a) implements <see cref="IOwnedEntity"/>,
///     (b) exposes an <c>OwnerId</c> property auto-populated from
///     <c>ICurrentUser.Id</c> on create, and (c) registers an OwnershipFilter
///     that trims reads to the caller's rows (except when the caller holds
///     the <c>{boundary}.{entity}.view-all</c> permission).
///
///     This sample omits the [Entity]/[HasOwner] attributes and implements
///     <see cref="IOwnedEntity"/> by hand because the SG's row-level-security
///     pipeline expects FilterMapRegistry / QueryFilterRegistration to be
///     emitted at host level — not something a lone console sample can boot.
///     The pattern the user sees (writer records OwnerId, reader filters by
///     it, admin bypasses) is identical.
/// </summary>
public sealed class OwnedNote : IOwnedEntity
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = "";
    public required string Title { get; set; }
    public string? Body { get; set; }
}
