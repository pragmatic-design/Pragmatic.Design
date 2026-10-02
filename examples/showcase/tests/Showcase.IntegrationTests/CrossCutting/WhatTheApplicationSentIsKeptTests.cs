using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A notification the application sent leaves a record, in the application's own database.
/// </summary>
/// <remarks>
///     <para>
///         <c>UseEfCoreStore()</c> registers a <c>DbContext</c> of the package's own, and a composed
///         host creates one context per <b>declared database</b> — the migration context — and nothing
///         else. So <c>__Notifications</c> was never created and the first write failed at run time. A
///         second context against the same database cannot fix it either: <c>EnsureCreated</c> is
///         all-or-nothing per database, so it finds the database already there and creates none of its
///         tables — which <c>PrivacyDbContext</c>'s own remark had said all along.
///     </para>
///     <para>
///         ⚠️ The answer was already in the tree, and five packages out of six used it: Audit, Privacy,
///         Cryptography, Messaging and the SQL transport each keep their tables in the application's
///         database. <c>[StoresNotifications]</c> on <c>BookingBoundary</c> is the declaration that
///         turns it on, in the shape <c>[EnableOutbox]</c> and <c>[EnableSagaPersistence]</c> already
///         had.
///     </para>
///     <para>
///         ⚠️ <b>And it took two halves.</b> Mapping the table into the boundary's own
///         <c>DbContext</c> is not enough: the schema an application creates comes from the
///         <b>migration</b> context, whose tables are hand-mirrored per package in
///         <c>SchemaMetadataTransform</c>. With only the first half this case failed with
///         <c>42P01: relation "__Notifications" does not exist</c> — measured, which is how the
///         second half was found.
///     </para>
///     <para>
///         Measured again by removal once it was green, because a case that is green on its first run
///         has proved nothing yet: commenting out the mirror's one call site turns this suite from
///         <b>676 passed</b> into <b>3 failed, 673 passed</b> — this case with a
///         <c>DbUpdateException</c>, and two more that went down with it —
///         <c>NotificationDeliveryTests</c>, which sends through the same store, and
///         <c>JobInfrastructureTests</c>, whose mechanism was not chased. Restoring it returns 676.
///     </para>
///     <para>
///         Nothing in this suite's fixture creates that schema by hand, which is the point: the table
///         comes from the application's own initialisation, with the application's tables.
///     </para>
/// </remarks>
public sealed class WhatTheApplicationSentIsKeptTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <remarks>
    ///     ⚠️ <c>EnqueueAsync</c> and not <c>SendAsync</c>, for two reasons that are about this suite
    ///     and not about the store: <c>SendAsync</c> returns an id it minted for the send, not the id
    ///     of any row, so there is nothing to read back by; and the host's only channel is SMTP against
    ///     <c>localhost</c>, so every delivery here fails and the result carries no id at all. Enqueue
    ///     writes the row itself, before the worker touches anything — which is exactly the write this
    ///     story is about.
    /// </remarks>
    [Fact]
    public async Task ANotificationTheApplicationSent_LeavesARecordInTheApplicationDatabase()
    {
        await using var scope = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var store = scope.ServiceProvider.GetRequiredService<INotificationStore>();

        var subject = $"Your stay {Guid.NewGuid():N}";
        var queued = await notifications.EnqueueAsync(new NotificationRequest
        {
            Audience = NotificationAudience.EndUser,
            Recipient = NotificationRecipient.Direct($"kept.{Guid.NewGuid():N}@showcase.test"),
            Content = new NotificationContent
            {
                Subject = subject,
                Body = "A record of this is what the case reads back.",
            },
        });

        queued.NotificationId.Should().NotBeNull(
            "the write to __Notifications is what allocates the record's id");

        var record = await store.GetByIdAsync(queued.NotificationId!.Value);

        record.Should().NotBeNull("the row is there to be read back, through the same store that wrote it");
        record!.Subject.Should().Be(subject,
            "the row carries what was sent — which is also the assertion that the hand-written schema "
            + "mirror agrees with the EF model, since a missing or mistyped column would have failed "
            + "the insert");
    }

    /// <summary>
    ///     The control: the store is the EF one, not the in-memory default.
    /// </summary>
    /// <remarks>
    ///     Without it the case above is satisfied by <c>InMemoryNotificationStore</c>, which keeps a
    ///     record perfectly well and needs no table at all — so it would pass on exactly the
    ///     configuration this issue is about.
    /// </remarks>
    [Fact]
    public void TheStoreIsTheOneBackedByTheDatabase()
    {
        using var scope = Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<INotificationStore>()
            .GetType().Name.Should().Be("EfCoreNotificationStore");
    }
}
