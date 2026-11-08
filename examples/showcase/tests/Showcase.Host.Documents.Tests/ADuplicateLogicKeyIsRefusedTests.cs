using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Documents.Tests;

/// <summary>
///     A <c>[LogicKey]</c> is unique on the documents engine as on EF Core and the Sql engine (#607): <c>Property.Code</c> is one, and a second
///     property with the same code answers 409. Before, the documents schema gave it no unique index and the second write was accepted.
/// </summary>
[Collection(DocumentsHostCollection.Name)]
public sealed class ADuplicateLogicKeyIsRefusedTests(DocumentsHostFixture fixture)
{
    [Fact]
    public async Task A_second_property_with_the_same_code_answers_409()
    {
        var code = $"DUP-{Guid.NewGuid():N}"[..12];

        var first = await Caller().PostAsJsonAsync("/api/properties", new { code, name = "Duplicate Probe", city = "Rome", country = "IT", starRating = 3 });
        var second = await Caller().PostAsJsonAsync("/api/properties", new { code, name = "Duplicate Probe", city = "Rome", country = "IT", starRating = 3 });
        var other = await Caller().PostAsJsonAsync("/api/properties", new { code = code + "X", name = "Duplicate Probe", city = "Rome", country = "IT", starRating = 3 });

        first.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await first.Content.ReadAsStringAsync());
        second.StatusCode.Should().Be(HttpStatusCode.Conflict, "Body: {0}", await second.Content.ReadAsStringAsync());
        other.StatusCode.Should().Be(HttpStatusCode.Created, "the control: another code is written. Body: {0}", await other.Content.ReadAsStringAsync());
    }

    /// <summary>A caller the host accepts: a tenant, an identity and every permission.</summary>
    private HttpClient Caller()
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "engine-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", "engine-user");
        client.DefaultRequestHeaders.Add("X-User-Name", "Documents host test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "*");
        return client;
    }
}
