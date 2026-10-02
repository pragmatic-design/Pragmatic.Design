using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Jobs;
using Pragmatic.Storage;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
/// E2E tests for the auto-generated [HasAttachments] trait endpoints on <c>Reservation</c>:
/// multipart upload → metadata read → list → delete, against a real database and real file storage.
/// </summary>
/// <remarks>
///     The attachment metadata table is generated twice — as an EF configuration and as a
///     migrations schema — and only a real database proves the two agree (the key column is
///     <c>PersistenceId</c>, and the abstract <c>ParentEntityId</c> from <c>AttachmentBase</c> is
///     not a column at all).
/// </remarks>
public class AttachmentEndpointTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // Minimal PDF: header + EOF marker. Enough for an extension/content-type check.
    private static readonly byte[] FakePdf =
        "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF"u8.ToArray();

    private const string ReservationsUrl = "/api/reservations";

    private static string AttachmentsUrl(Guid reservationId) =>
        $"/api/booking/reservations/{reservationId}/attachments";

    private static string AttachmentUrl(Guid reservationId, Guid attachmentId) =>
        $"/api/booking/reservations/{reservationId}/attachments/{attachmentId}";

    private static string AttachmentContentUrl(Guid reservationId, Guid attachmentId) =>
        $"{AttachmentUrl(reservationId, attachmentId)}/content";

    [Fact]
    public async Task UploadAttachment_ReturnsCreated_AndStoresTheFile()
    {
        var reservationId = await CreateReservation();

        using var content = BuildUpload(FakePdf, "contract.pdf", "application/pdf", "Signed contract");
        var response = await Client.PostAsync(AttachmentsUrl(reservationId), content);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, "upload response was: {0}", body);

        var attachmentId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        attachmentId.Should().NotBeEmpty("the key must be value-generated, not left at Guid.Empty");

        // The metadata row must point at a file that actually reached storage. The URI itself is no
        // longer exposed to the client, so verify it through the endpoint that serves the bytes.
        var download = await GetRawAsync(AttachmentContentUrl(reservationId, attachmentId));
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(FakePdf);
    }

    /// <summary>
    ///     The storage URI names the provider and the bucket layout, and on some providers it is a
    ///     directly reachable URL. It has no business crossing the HTTP boundary now that the bytes
    ///     have their own endpoint.
    /// </summary>
    [Fact]
    public async Task GetAttachment_DoesNotExposeTheStorageUri()
    {
        var reservationId = await CreateReservation();
        var attachmentId = await Upload(reservationId, "private.pdf");

        var json = await GetJson(AttachmentUrl(reservationId, attachmentId));

        json.TryGetProperty("storageUri", out _).Should().BeFalse();
        var raw = json.GetRawText();
        raw.Should().NotContain("storageUri").And.NotContain("StorageUri");
    }

    [Fact]
    public async Task DownloadAttachment_ReturnsTheExactBytes_WithContentTypeAndFileName()
    {
        var reservationId = await CreateReservation();
        var attachmentId = await Upload(reservationId, "contract.pdf");

        var response = await GetRawAsync(AttachmentContentUrl(reservationId, attachmentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf",
            "the content type comes from the recorded metadata, not from a guess");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("contract.pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(FakePdf, "the response body must be the stored file, byte for byte");
    }

    /// <summary>
    ///     The IDOR test. Both ids are in the route and the lookup filters on both: knowing the id of
    ///     somebody else's attachment must not be enough to download it from a parent you can read.
    /// </summary>
    [Fact]
    public async Task DownloadAttachment_FromAnotherReservation_Returns404()
    {
        var mine = await CreateReservation();
        var theirs = await CreateReservation();
        var theirAttachment = await Upload(theirs, "confidential.pdf");

        // Same caller, same permission, someone else's attachment id under MY reservation.
        var response = await GetRawAsync(AttachmentContentUrl(mine, theirAttachment));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Sanity check: the very same id downloads fine under its own parent, so the 404 above is
        // the parent scoping and not a broken route.
        var owned = await GetRawAsync(AttachmentContentUrl(theirs, theirAttachment));
        owned.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DownloadAttachment_AfterSoftDelete_Returns404()
    {
        var reservationId = await CreateReservation();
        var attachmentId = await Upload(reservationId, "obsolete.pdf");

        (await DeleteAsync(AttachmentUrl(reservationId, attachmentId)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await GetRawAsync(AttachmentContentUrl(reservationId, attachmentId));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The blob is deliberately still there — the soft delete is reversible, so the bytes must
        // outlive the row's IsDeleted flag until the retention job decides otherwise.
        var storage = Services.GetRequiredService<IFileStorage>();
        (await storage.ExistsAsync(ToUri(await StorageUriOf(attachmentId)))).Should().BeTrue();
    }

    /// <summary>
    ///     The generated <c>[HasAttachments(PurgeDeletedAfterDays = 30)]</c> retention job, run against
    ///     the real database and the real storage: blob gone first, row gone second.
    /// </summary>
    [Fact]
    public async Task PurgeJob_RemovesTheBlobAndTheRow_OnceTheRetentionWindowHasPassed()
    {
        var reservationId = await CreateReservation();
        var expiredId = await Upload(reservationId, "expired.pdf");
        var recentId = await Upload(reservationId, "recent.pdf");

        await DeleteAsync(AttachmentUrl(reservationId, expiredId));
        await DeleteAsync(AttachmentUrl(reservationId, recentId));

        var expiredUri = ToUri(await StorageUriOf(expiredId));
        var recentUri = ToUri(await StorageUriOf(recentId));

        // Backdate one of the two past the 30-day window declared on the entity.
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var row = await db.Set<ReservationAttachment>().IgnoreQueryFilters()
                .SingleAsync(a => a.Id == expiredId);
            row.DeletedAt = DateTimeOffset.UtcNow.AddDays(-60);
            await db.SaveChangesAsync();
        }

        using (var scope = Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<PurgeReservationAttachmentsJob>();
            var context = new JobContext(
                Guid.NewGuid(), typeof(PurgeReservationAttachmentsJob).FullName!,
                DateTimeOffset.UtcNow, Attempt: 0, MaxAttempts: 1);
            await job.ExecuteAsync(context, CancellationToken.None);
        }

        var storage = Services.GetRequiredService<IFileStorage>();
        (await storage.ExistsAsync(expiredUri)).Should().BeFalse("the blob past the window is reclaimed");
        (await storage.ExistsAsync(recentUri)).Should().BeTrue("a fresh soft delete stays restorable");

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var rows = await db.Set<ReservationAttachment>().IgnoreQueryFilters()
                .Where(a => a.ReservationId == reservationId)
                .Select(a => a.Id)
                .ToListAsync();

            rows.Should().NotContain(expiredId, "the row goes only after its blob");
            rows.Should().Contain(recentId);
        }
    }

    [Fact]
    public async Task DownloadAttachment_WithoutReadPermission_Returns403()
    {
        var reservationId = await CreateReservation();
        var attachmentId = await Upload(reservationId, "gated.pdf");

        // Upload permission only: the content must be gated on the same slug as the metadata.
        var client = CreateClientWithPermissions("booking.reservation.attachments.upload");

        var response = await client.GetAsync(AttachmentContentUrl(reservationId, attachmentId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAttachment_AfterUpload_ReturnsMetadata()
    {
        var reservationId = await CreateReservation();
        var attachmentId = await Upload(reservationId, "invoice.pdf", "Q3 invoice");

        var json = await GetJson(AttachmentUrl(reservationId, attachmentId));

        json.GetProperty("id").GetGuid().Should().Be(attachmentId);
        json.GetProperty("reservationId").GetGuid().Should().Be(reservationId);
        json.GetProperty("fileName").GetString().Should().Be("invoice.pdf");
        json.GetProperty("contentType").GetString().Should().Be("application/pdf");
        json.GetProperty("fileSize").GetInt64().Should().Be(FakePdf.Length);
        json.GetProperty("description").GetString().Should().Be("Q3 invoice");
        json.GetProperty("uploadedBy").GetString().Should().Be("test-user");
    }

    [Fact]
    public async Task ListAttachments_AfterTwoUploads_ReturnsBoth_AndOnlyOwnReservation()
    {
        // Two rows on one parent: a key left ValueGeneratedNever would collide on the second insert.
        var mine = await CreateReservation();
        var other = await CreateReservation();
        await Upload(mine, "first.pdf");
        await Upload(mine, "second.pdf");
        await Upload(other, "theirs.pdf");

        var json = await GetJson($"{AttachmentsUrl(mine)}?page=1&pageSize=10");

        json.GetProperty("totalCount").GetInt32().Should().Be(2);
        json.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("fileName").GetString())
            .Should().BeEquivalentTo("first.pdf", "second.pdf");
    }

    [Fact]
    public async Task DeleteAttachment_SoftDeletes_AndDisappearsFromReads()
    {
        var reservationId = await CreateReservation();
        var attachmentId = await Upload(reservationId, "obsolete.pdf");

        var delete = await DeleteAsync(AttachmentUrl(reservationId, attachmentId));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await GetRawAsync(AttachmentUrl(reservationId, attachmentId));
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await GetJson(AttachmentsUrl(reservationId));
        list.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task UploadAttachment_WithDisallowedExtension_Returns415()
    {
        // [HasAttachments(AllowedExtensions = ".pdf,.jpg,.png,.docx")] — enforced at the HTTP boundary.
        var reservationId = await CreateReservation();

        using var content = BuildUpload(FakePdf, "payload.exe", "application/octet-stream");
        var response = await Client.PostAsync(AttachmentsUrl(reservationId), content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    /// <summary>
    ///     A caller who may not see the reservation may not list its attachments either.
    /// </summary>
    /// <remarks>
    ///     The generated list query filters children by the parent id it was handed and checks the
    ///     trait's own read permission — it never asked whether the caller may see that parent, and
    ///     the child carries no restriction of its own. Anyone holding
    ///     <c>booking.reservation.attachments.read</c> could therefore enumerate file names, sizes and
    ///     uploaders for a reservation belonging to someone else, needing only its id. Found by two
    ///     independent reviews of a lab application; the Showcase carried the same hole on six
    ///     endpoints and 507 E2E tests never touched it, because none of them asked for the children
    ///     of a parent it could not see.
    ///     <para>
    ///         The reservation is <c>[HasOwner]</c>, so the child now carries the owner's predicate
    ///         read through its navigation. An empty page is the right answer rather than a 403: the
    ///         caller does hold the permission, and 404-vs-empty must not reveal that the parent exists.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ListAttachments_ForAReservationTheCallerCannotSee_ReturnsNothing()
    {
        var reservationId = await CreateReservation();
        await Upload(reservationId, "confidential.pdf");

        // Authenticated, holds the trait's read permission, owns nothing and cannot bypass.
        using var stranger = CreateClientWithPermissions("booking.reservation.attachments.read");

        var response = await stranger.GetAsync($"{AttachmentsUrl(reservationId)}?page=1&pageSize=10");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the permission is held — response was: {0}", body);

        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("totalCount").GetInt32().Should().Be(0,
            "the reservation belongs to someone else, so its attachments are not this caller's to count");
        json.GetProperty("items").GetArrayLength().Should().Be(0,
            "not even the file name may leak — that is the whole of what the endpoint gave away");
    }

    /// <summary>
    ///     The counterpart: the filter must not empty the list for everyone.
    /// </summary>
    [Fact]
    public async Task ListAttachments_ForTheOwner_StillReturnsThem()
    {
        var reservationId = await CreateReservation();
        await Upload(reservationId, "mine.pdf");

        var json = await GetJson($"{AttachmentsUrl(reservationId)}?page=1&pageSize=10");

        json.GetProperty("totalCount").GetInt32().Should().Be(1,
            "the owner sees their own attachments — a filter that blocks everyone is not a fix");
    }

    /// <summary>
    ///     The parent's bypass permission carries over: whoever may see every reservation may list
    ///     every attachment.
    /// </summary>
    [Fact]
    public async Task ListAttachments_WithTheParentsBypassPermission_SeesThem()
    {
        var reservationId = await CreateReservation();
        await Upload(reservationId, "audited.pdf");

        using var admin = CreateClientWithPermissions(
            "booking.reservation.attachments.read",
            "booking.reservation.view-all");

        var response = await admin.GetAsync($"{AttachmentsUrl(reservationId)}?page=1&pageSize=10");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "response was: {0}", body);
        JsonDocument.Parse(body).RootElement.GetProperty("totalCount").GetInt32().Should().Be(1,
            "the child borrows the parent's bypass, so the two give the same answer");
    }

    [Fact]
    public async Task ListAttachments_WithoutReadPermission_Returns403()
    {
        // Grant the upload permission specifically: proving that holding it does NOT grant reads is
        // the point. A made-up slug would deny the request for the wrong reason and still pass.
        var client = CreateClientWithPermissions("booking.reservation.attachments.upload");

        var response = await client.GetAsync(AttachmentsUrl(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static MultipartFormDataContent BuildUpload(
        byte[] bytes, string fileName, string contentType = "application/pdf", string? description = null)
    {
        var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        // Field name must match the generated endpoint's IFormFile parameter ("file").
        form.Add(fileContent, "file", fileName);

        if (description is not null)
            form.Add(new StringContent(description), "description");

        return form;
    }

    private async Task<Guid> Upload(Guid reservationId, string fileName, string? description = null)
    {
        using var content = BuildUpload(FakePdf, fileName, description: description);
        var response = await Client.PostAsync(AttachmentsUrl(reservationId), content);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, "upload response was: {0}", body);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private async Task<JsonElement> GetJson(string url)
    {
        var response = await GetRawAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0} returned: {1}", url, body);
        return JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
    }

    // As IFileStorage.SaveAsync says to read a stored URI back. Trying UriKind.Absolute first would make
    // "/files/…" the absolute file:///files/… on Linux, which the disk storage refuses as foreign.
    private static Uri ToUri(string value) => new(value, UriKind.RelativeOrAbsolute);

    /// <summary>
    ///     Reads the storage URI straight from the database: it is deliberately not exposed over HTTP,
    ///     but the tests still need it to look at the blob behind an attachment.
    /// </summary>
    private async Task<string> StorageUriOf(Guid attachmentId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        return await db.Set<ReservationAttachment>()
            .IgnoreQueryFilters()
            .Where(a => a.Id == attachmentId)
            .Select(a => a.StorageUri)
            .SingleAsync();
    }

    /// <summary>The derived file's address, read the same way and for the same reason.</summary>
    private async Task<string?> ThumbnailUriOf(Guid attachmentId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        return await db.Set<ReservationAttachment>()
            .IgnoreQueryFilters()
            .Where(a => a.Id == attachmentId)
            .Select(a => a.ThumbnailUri)
            .SingleAsync();
    }

    /// <summary>
    ///     A real PNG, larger than the 200-square bound the entity declares.
    /// </summary>
    /// <remarks>
    ///     Built rather than embedded: <c>QrCode.GeneratePng</c> is the one call in Pragmatic.Imaging
    ///     that produces a valid image from nothing, and larger than the bound because a thumbnail
    ///     never upscales — an image that already fits would come back the same size and "smaller"
    ///     would assert nothing.
    /// </remarks>
    private static byte[] APhoto(uint width = 400, uint height = 320)
    {
        using var pipeline = Pragmatic.Imaging.ImagePipeline.Load(
            Pragmatic.Imaging.QrCode.GeneratePng("room", moduleSize: 1, margin: 0));
        pipeline.Resize(width, height, Pragmatic.Imaging.ResizeFilter.Nearest);
        return pipeline.Encode(Pragmatic.Imaging.ImageFormat.Png);
    }

    /// <summary>
    ///     An attached photo gets a preview, and the retention job reclaims both files.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The purge half is the one that cannot be seen any other way. The row is the only
    ///         thing that names either file, so a purge that deletes the original and removes the row
    ///         leaves the thumbnail addressable by nothing: no query returns it, and the job that
    ///         would have deleted it will never see it again. Nothing fails, and the store grows.
    ///     </para>
    ///     <para>
    ///         The upload half is asserted by decoding, not by counting bytes — a truncation also
    ///         produces a shorter file, and it is what a half-written blob looks like.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task PurgeJob_ReclaimsTheThumbnailAsWellAsTheOriginal()
    {
        var reservationId = await CreateReservation();

        using var upload = BuildUpload(APhoto(), "room.png", "image/png");
        var response = await Client.PostAsync(AttachmentsUrl(reservationId), upload);
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "upload response was: {0}", await response.Content.ReadAsStringAsync());

        var attachmentId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        var thumbnailUri = await ThumbnailUriOf(attachmentId);
        thumbnailUri.Should().NotBeNull("an image gets a preview derived where it is stored");

        var storage = Services.GetRequiredService<IFileStorage>();
        var originalUri = ToUri(await StorageUriOf(attachmentId));
        var derivedUri = ToUri(thumbnailUri!);

        await using (var stream = await storage.GetAsync(derivedUri))
        {
            stream.Should().NotBeNull();
            using var buffer = new MemoryStream();
            await stream!.CopyToAsync(buffer);

            var info = Pragmatic.Imaging.ImageInfo.FromBytes(buffer.ToArray());
            info.Width.Should().BeLessThan(400u, "it was resized, not merely re-encoded");
            info.Height.Should().BeLessThan(320u);
            info.Width.Should().BeLessThanOrEqualTo(200u, "the declared bound is the bound");
            info.Height.Should().BeLessThanOrEqualTo(200u);
        }

        await DeleteAsync(AttachmentUrl(reservationId, attachmentId));

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var row = await db.Set<ReservationAttachment>().IgnoreQueryFilters()
                .SingleAsync(a => a.Id == attachmentId);
            row.DeletedAt = DateTimeOffset.UtcNow.AddDays(-60);
            await db.SaveChangesAsync();
        }

        using (var scope = Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<PurgeReservationAttachmentsJob>();
            await job.ExecuteAsync(
                new JobContext(Guid.NewGuid(), typeof(PurgeReservationAttachmentsJob).FullName!,
                    DateTimeOffset.UtcNow, Attempt: 0, MaxAttempts: 1),
                CancellationToken.None);
        }

        (await storage.ExistsAsync(derivedUri)).Should().BeFalse(
            "the derived file goes with the record; the row is the only thing that named it");
        (await storage.ExistsAsync(originalUri)).Should().BeFalse(
            "and the original, which is what the job already reclaimed");
    }

    private async Task<Guid> CreateReservation()
    {
        var guestBody = new { firstName = "AT", lastName = "Guest", email = $"at.{Guid.NewGuid():N}@test.com" };
        var guest = await PostAsync<JsonElement>("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var propBody = new { code = $"AT-{Guid.NewGuid():N}"[..12], name = "AttachProp", city = "Rome", country = "IT", starRating = 3 };
        var prop = await PostAsync("/api/properties", propBody);
        prop.EnsureSuccessStatusCode();
        var propJson = await prop.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = propJson.GetProperty("id").GetGuid();

        var rtBody = new { propertyId, name = "AT Room", code = "ATR", baseRate = 100m, totalRooms = 5 };
        var roomType = await PostAsync<JsonElement>("/api/room-types", rtBody);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        var reservationBody = new
        {
            request = new
            {
                guestId, propertyId, roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 2
            }
        };
        var response = await PostAsync($"{ReservationsUrl}?api-version=1.0", reservationBody);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
