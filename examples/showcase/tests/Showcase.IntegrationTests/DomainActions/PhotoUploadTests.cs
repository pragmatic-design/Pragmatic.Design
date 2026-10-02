using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Storage;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     End-to-end coverage for the file-storage flow: uploading a property photo through the
///     generated <c>UploadPropertyPhotoAction</c> endpoint (multipart/form-data → IFileStorage).
///     Exercises:
///       - [FromForm] IFormFile binding in a DomainAction endpoint
///       - IFileStorage.SaveAsync with a FLAT container (Azure-portable, no '/' in container name)
///       - The returned URI resolves back to a stored file via IFileStorage.ExistsAsync
/// </summary>
public class PhotoUploadTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // Minimal valid-enough JPEG: SOI marker + APP0/JFIF header bytes + EOI marker.
    private static readonly byte[] FakeJpeg =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46,
        0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
        0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    [Fact]
    public async Task UploadPropertyPhoto_ValidFile_ReturnsUriAndStoresFile()
    {
        var propertyId = await CreatePropertyAsync();

        using var content = BuildMultipartPhoto(FakeJpeg, "beach.jpg", caption: "Ocean view");

        var response = await Client.PostAsync(
            $"/api/properties/{propertyId}/photos", content);

        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.OK, HttpStatusCode.Created],
            "a valid photo upload should succeed");

        // DomainAction<Uri> → the body is the stored file URI serialized as a JSON string.
        var uriString = await ReadUriStringAsync(response);
        uriString.Should().NotBeNullOrWhiteSpace("the action must return the stored file URI");

        // Flat container (finding #14): the path segment must be "property-photos", never a nested
        // "properties/{id}/photos" that Azure Blob would reject as a container name.
        uriString.Should().Contain("property-photos");
        uriString.Should().NotContain("properties/", "the container must stay flat (no slashes)");

        // End-to-end: the URI the API returned must resolve to an actually-stored file.
        var storage = Services.GetRequiredService<IFileStorage>();
        var exists = await storage.ExistsAsync(ToUri(uriString!));
        exists.Should().BeTrue("the returned URI must point at a file that was physically stored");
    }

    [Fact]
    public async Task UploadPropertyPhoto_NonExistentProperty_ReturnsNotFound()
    {
        // Caption must be supplied: the generated endpoint binds it as a required form field.
        using var content = BuildMultipartPhoto(FakeJpeg, "ghost.jpg", caption: "no-property");

        var response = await Client.PostAsync(
            $"/api/properties/{Guid.NewGuid()}/photos", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "uploading a photo for a non-existent property should return 404");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static MultipartFormDataContent BuildMultipartPhoto(
        byte[] bytes, string fileName, string? caption = null)
    {
        var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        // Field name must match the [FromForm] IFormFile property ("Photo").
        form.Add(fileContent, "Photo", fileName);

        if (caption is not null)
            form.Add(new StringContent(caption), "Caption");

        return form;
    }

    private static async Task<string?> ReadUriStringAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        // The value comes back either as a bare JSON string ("\"/files/...\"") or, if the body is not
        // JSON-wrapped, as the plain path. Normalise both to the underlying URI string.
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            return JsonSerializer.Deserialize<string>(trimmed, JsonOptions);
        return trimmed;
    }

    // As IFileStorage.SaveAsync says to read a stored URI back. Trying UriKind.Absolute first made
    // "/files/…" the absolute file:///files/… on Linux, which the disk storage refuses as foreign.
    private static Uri ToUri(string value) => new(value, UriKind.RelativeOrAbsolute);

    private async Task<Guid> CreatePropertyAsync()
    {
        var body = new
        {
            code = $"PH-{Guid.NewGuid():N}"[..12],
            name = $"PhotoProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 4
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("id").GetGuid();
    }
}
