using System.Collections.Generic;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Showcase.Billing;
using Showcase.Booking;
using Showcase.Host;
using Xunit;

namespace Showcase.Tests.Unit;

/// <summary>
///     What the role registry says a role grants, when the grant is a list the module owns.
/// </summary>
/// <remarks>
///     <para>
///         The host's roles are composed twice over: at runtime in <c>Program.cs</c>, where
///         <c>MapRole</c> takes the permissions, and at compile time in <c>DefaultPermissions</c>,
///         which is what the generator catalogues into <see cref="RoleRegistry" />. Only the first
///         was filled in, so the registry — the list a role screen reads, and the only answer
///         available before the application starts — said `billing-clerk` grants nothing.
///     </para>
///     <para>
///         ⚠️ Reading the grant from the module is what makes it a cross-assembly read, and that is
///         the case <c>[PermissionSet]</c> exists for: a referenced assembly is metadata, a
///         <c>static</c> list has no constant value there, and without the attribute the catalogue
///         says <b>nothing</b> while the runtime grants every entry. The values travel as
///         <c>[assembly: PermissionSetValues]</c>, written by the generator onto the assembly that
///         owns the list.
///     </para>
/// </remarks>
public class WhatARoleGrantsCrossesTheAssemblyBoundary
{
    private static IReadOnlyList<string> GrantOf(string role)
        => RoleRegistry.All.Single(r => r.Name == role).DefaultPermissions;

    [Fact]
    public void TheClerksGrant_NamesWhatEachModulePublished()
    {
        var grant = GrantOf("billing-clerk");

        grant.Should().Contain(BillingPermissions.Invoice.Read,
            "the clerk's own module publishes the billing half of the grant");
        grant.Should().Contain(BookingPermissions.Reservation.Read,
            "and the booking half comes from a list Showcase.Booking owns, read across the assembly "
            + "boundary — which is the whole reason the list is marked");
    }

    /// <summary>
    ///     The control: the registry publishes the lists and not everything.
    /// </summary>
    /// <remarks>
    ///     Without it, "the grant contains the booking read" is satisfied by a catalogue that answers
    ///     every permission in the application — which is exactly what a wildcard grant looks like,
    ///     and the clerk's description says <em>reads</em> booking data.
    /// </remarks>
    [Fact]
    public void TheClerksGrant_StopsAtTheBookingReads()
    {
        var grant = GrantOf("billing-clerk");

        grant.Should().NotContain(BookingPermissions.Reservation.Delete,
            "a clerk who reads booking data has not been given a booking write");
    }
}
