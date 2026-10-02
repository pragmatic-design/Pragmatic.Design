using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Storage;
using Pragmatic.Testing.Assertions;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     An attachment upload whose commit happened but whose acknowledgement was lost keeps the file its
///     row points at.
/// </summary>
/// <remarks>
///     <para>
///         The generated upload stores the blob, then commits the row, and on any exception from the
///         commit deletes the blob and rethrows. A commit can throw after it has succeeded — the connection
///         drops before the server's answer arrives — and then the row stays and points at a file that is
///         gone. A retrying execution strategy cannot tell the two apart either; EF offers
///         <c>verifySucceeded</c> for exactly that.
///     </para>
///     <para>
///         The loss is simulated by an interceptor on the attachment insert: after the transaction that
///         carried it commits, or right after the statement when EF runs it without one. The control
///         refuses the insert before it runs, which is a commit that genuinely did not happen: there the
///         upload must still fail and leave no row.
///     </para>
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AnUploadWhoseCommitIsNotAcknowledgedTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly byte[] FakePdf =
        "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AttachmentInsertFault _fault = new();
    private ShowcaseWebFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ShowcaseWebFactory(fixture, extraServices: services =>
        {
            services.AddSingleton<IInterceptor>(new AttachmentInsertFault.Commands(_fault));
            services.AddSingleton<IInterceptor>(new AttachmentInsertFault.Transactions(_fault));
        });

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        _client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        _client.DefaultRequestHeaders.Add("X-User-Name", "Lost Acknowledgement Test");
        _client.DefaultRequestHeaders.Add("X-User-Permissions", "catalog.*,booking.*");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ACommittedRowWhoseAcknowledgementIsLost_KeepsItsFile_AndAnswersTheId()
    {
        var reservationId = await CreateReservation();

        _fault.Mode = FaultMode.LoseTheAcknowledgement;
        var response = await _client.PostAsync(AttachmentsUrl(reservationId), Upload());
        _fault.Mode = FaultMode.None;

        _fault.Fired.Should().Be(1, "the acknowledgement of the committed insert was lost exactly once");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            $"the row committed, so the upload happened — answering an error would invite a second one: {body}");

        var attachmentId = JsonSerializer.Deserialize<Guid>(body, JsonOptions);
        var storageUri = await StorageUriOf(attachmentId);
        storageUri.Should().NotBeNull("the row is there: the commit did happen");

        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        (await storage.ExistsAsync(ToUri(storageUri!))).Should().BeTrue(
            "and the file it points at is still there — deleting it would leave a row naming nothing");
    }

    /// <summary>
    ///     The control: an insert that never ran is a failed upload, and stays one.
    /// </summary>
    /// <remarks>
    ///     Without it the test above is satisfied by an upload that answers success whatever happened.
    /// </remarks>
    [Fact]
    public async Task AnInsertThatNeverRan_FailsTheUpload_AndLeavesNoRow()
    {
        var reservationId = await CreateReservation();

        _fault.Mode = FaultMode.RefuseTheInsert;
        var response = await _client.PostAsync(AttachmentsUrl(reservationId), Upload());
        _fault.Mode = FaultMode.None;

        _fault.Fired.Should().Be(1, "the insert was refused before it ran");
        response.IsSuccessStatusCode.Should().BeFalse("nothing was committed, so nothing was uploaded");
        (await RowsFor(reservationId)).Should().Be(0);
    }

    private static string AttachmentsUrl(Guid reservationId) =>
        $"/api/booking/reservations/{reservationId}/attachments";

    private static MultipartFormDataContent Upload()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(FakePdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "contract.pdf");
        return form;
    }

    // As IFileStorage.SaveAsync says to read a stored URI back. Trying UriKind.Absolute first made
    // "/files/…" the absolute file:///files/… on Linux, which the disk storage refuses as foreign.
    private static Uri ToUri(string value) => new(value, UriKind.RelativeOrAbsolute);

    private async Task<string?> StorageUriOf(Guid attachmentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        return await db.Set<ReservationAttachment>()
            .IgnoreQueryFilters()
            .Where(a => a.Id == attachmentId)
            .Select(a => a.StorageUri)
            .SingleOrDefaultAsync();
    }

    private async Task<int> RowsFor(Guid reservationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        return await db.Set<ReservationAttachment>()
            .IgnoreQueryFilters()
            .CountAsync(a => a.ReservationId == reservationId);
    }

    private async Task<JsonElement> PostJson(string url, object body)
    {
        var response = await _client.PostAsJsonAsync(url, body, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<Guid> CreateReservation()
    {
        var guest = await PostJson("/api/guests",
            new { firstName = "LA", lastName = "Guest", email = $"la.{Guid.NewGuid():N}@test.com" });
        var property = await PostJson("/api/properties",
            new { code = $"LA-{Guid.NewGuid():N}"[..12], name = "AckProp", city = "Rome", country = "IT", starRating = 3 });
        var propertyId = property.GetProperty("id").GetGuid();
        var roomType = await PostJson("/api/room-types",
            new { propertyId, name = "LA Room", code = "LAR", baseRate = 100m, totalRooms = 5 });

        var response = await _client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId,
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 2
            }
        }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private enum FaultMode
    {
        None,
        LoseTheAcknowledgement,
        RefuseTheInsert
    }

    /// <summary>Fails the attachment insert, before it runs or after it has committed.</summary>
    private sealed class AttachmentInsertFault
    {
        private readonly ConcurrentDictionary<DbTransaction, byte> _carryingTheInsert = new();
        private int _fired;

        public volatile FaultMode Mode;

        public int Fired => _fired;

        private static bool IsTheInsert(DbCommand command)
            => command.CommandText.Contains("INSERT INTO \"ReservationAttachment", StringComparison.Ordinal);

        private Exception Fire(string what)
        {
            Interlocked.Increment(ref _fired);
            return new InvalidOperationException(what);
        }

        private void Refuse(DbCommand command)
        {
            if (Mode == FaultMode.RefuseTheInsert && IsTheInsert(command))
            {
                Mode = FaultMode.None;
                throw Fire("Simulated refusal of the attachment insert.");
            }
        }

        private void AfterTheInsert(DbCommand command)
        {
            if (Mode != FaultMode.LoseTheAcknowledgement || !IsTheInsert(command))
                return;

            // Inside a transaction the loss is the commit's acknowledgement; without one the statement
            // committed on its own, and losing its answer is the same failure.
            if (command.Transaction is { } transaction)
            {
                _carryingTheInsert[transaction] = 0;
                return;
            }

            Mode = FaultMode.None;
            throw Fire("Simulated loss of the acknowledgement of an autocommitted insert.");
        }

        private void AfterTheCommit(DbTransaction transaction)
        {
            if (_carryingTheInsert.TryRemove(transaction, out _))
            {
                Mode = FaultMode.None;
                throw Fire("Simulated loss of the commit's acknowledgement.");
            }
        }

        public sealed class Commands(AttachmentInsertFault fault) : DbCommandInterceptor
        {
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
            {
                fault.Refuse(command);
                return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
            }

            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                fault.Refuse(command);
                return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
            }

            public override ValueTask<DbDataReader> ReaderExecutedAsync(
                DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
                CancellationToken cancellationToken = default)
            {
                fault.AfterTheInsert(command);
                return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
            }

            public override ValueTask<int> NonQueryExecutedAsync(
                DbCommand command, CommandExecutedEventData eventData, int result,
                CancellationToken cancellationToken = default)
            {
                fault.AfterTheInsert(command);
                return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
            }
        }

        public sealed class Transactions(AttachmentInsertFault fault) : DbTransactionInterceptor
        {
            public override Task TransactionCommittedAsync(
                DbTransaction transaction, TransactionEndEventData eventData,
                CancellationToken cancellationToken = default)
            {
                fault.AfterTheCommit(transaction);
                return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
            }
        }
    }
}
