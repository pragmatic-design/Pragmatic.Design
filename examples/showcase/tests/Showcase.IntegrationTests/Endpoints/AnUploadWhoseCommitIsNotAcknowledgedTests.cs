using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;
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
///         The loss is simulated by an <see cref="ICommitObserver" />, which every engine calls after the
///         commit that carried the attachment and before the upload sees it. The control refuses the insert
///         before it runs, which is a commit that genuinely did not happen: there the upload must still fail
///         and leave no row. One file for EF Core, Sql and documents: the observer is the engines' common seam.
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
        _factory = new ShowcaseWebFactory(fixture, extraServices: services => services.AddSingleton<ICommitObserver>(_fault));

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
        attachmentId.Should().Be(_fault.Attachment!.Value, "the row the observer saw committed is the one the upload answers");
        var row = await AttachmentRows.FindAsync(_factory.Services, attachmentId);
        row.Should().NotBeNull("the row is there: the commit did happen");

        var storage = _factory.Services.GetRequiredService<IFileStorage>();
        (await storage.ExistsAsync(ToUri(row!.StorageUri))).Should().BeTrue(
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
        (await AttachmentRows.FindAsync(_factory.Services, _fault.Attachment!.Value)).Should().BeNull(
            "the row the observer refused was never written");
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

    /// <summary>Fails the attachment insert, before it is written or after it has committed.</summary>
    private sealed class AttachmentInsertFault : ICommitObserver
    {
        private int _fired;

        public volatile FaultMode Mode;

        public int Fired => _fired;

        /// <summary>The attachment the fault fired on.</summary>
        public Guid? Attachment { get; private set; }

        public void Writing(IReadOnlyList<CommitEntry> entries)
        {
            if (Mode == FaultMode.RefuseTheInsert && TheInsert(entries) is { } attachment)
                throw Fire(attachment, "Simulated refusal of the attachment insert.");
        }

        public void Committed(IReadOnlyList<CommitEntry> entries)
        {
            if (Mode == FaultMode.LoseTheAcknowledgement && TheInsert(entries) is { } attachment)
                throw Fire(attachment, "Simulated loss of the commit's acknowledgement.");
        }

        private static ReservationAttachment? TheInsert(IReadOnlyList<CommitEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry is { State: CommitEntryState.Added, Entity: ReservationAttachment attachment })
                    return attachment;
            }

            return null;
        }

        private InvalidOperationException Fire(ReservationAttachment attachment, string what)
        {
            Mode = FaultMode.None;
            Attachment = attachment.Id;
            Interlocked.Increment(ref _fired);
            return new InvalidOperationException(what);
        }
    }
}
