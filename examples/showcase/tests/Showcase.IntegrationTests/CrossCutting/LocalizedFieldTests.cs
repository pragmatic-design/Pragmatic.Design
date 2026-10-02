using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Showcase.Catalog;
using Showcase.Catalog.Dtos;
using Showcase.Catalog.Entities;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A localized field, declared on a mutation and read back in two cultures.
/// </summary>
/// <remarks>
///     <para>
///         <c>LocalizedString</c> was understood by the persistence half of the generator and by
///         nothing else that anyone had checked: the entity carries one and it is stored as JSON, and
///         the one application using the feature declared a <c>Dictionary&lt;string, string&gt;</c> on
///         its mutations and converted by hand. No case sent two cultures over HTTP and read one back.
///     </para>
///     <para>
///         ⚠️ <c>UserCultureTests</c> covers culture <em>resolution</em>, not localized columns, so
///         the two halves were tested separately and the join between them was not.
///     </para>
/// </remarks>
public class LocalizedFieldTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<Guid> APropertyDescribedAsync(object description)
    {
        var created = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"LOC{Guid.NewGuid():N}"[..12],
            name = "Localized Property",
            city = "Rimini",
            country = "IT",
            starRating = 4,
            description
        });

        return created.GetProperty("id").GetGuid();
    }

    private async Task<string?> DescriptionInAsync(Guid id, string culture)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/properties/{id}");
        request.Headers.Add("Accept-Language", culture);

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("description").GetString();
    }

    /// <summary>Two cultures in one request, and each one read back under its own header.</summary>
    [Fact]
    public async Task TwoCulturesGoIn_AndEachComesBackUnderItsOwnHeader()
    {
        var id = await APropertyDescribedAsync(new { en = "A beautiful hotel", it = "Un bell'albergo" });

        (await DescriptionInAsync(id, "en")).Should().Be("A beautiful hotel");
        (await DescriptionInAsync(id, "it")).Should().Be("Un bell'albergo",
            "the column holds both, and the read resolves the one the caller asked for");
    }

    /// <summary>And the published contract says it takes an object, not a string.</summary>
    /// <remarks>
    ///     A localized field published as <c>string</c> is the contract lying about what it accepts: a
    ///     client generated from it would send <c>"description": "…"</c> and be refused.
    /// </remarks>
    [Fact]
    public async Task TheContract_PublishesTheObject_NotAString()
    {
        var document = await GetAsync<JsonElement>("/openapi/v1.json");

        var localized = document
            .GetProperty("components").GetProperty("schemas")
            .GetProperty("LocalizedString");

        localized.GetProperty("type").GetString().Should().Be("object",
            "a culture-to-value map is an object; the members of the CLR type are not the payload");
        localized.GetProperty("additionalProperties").GetProperty("type").GetString().Should().Be("string",
            "and every value in it is a string");

        var field = document
            .GetProperty("components").GetProperty("schemas")
            .GetProperty("CreatePropertyMutationRequest")
            .GetProperty("properties")
            .GetProperty("description");

        field.ToString().Should().Contain("LocalizedString",
            "the field refers to that schema rather than describing something else");
    }

    /// <summary>
    ///     And the read carries every translation, not only the one the caller asked for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Description</c> answers the current culture, which is what a page shows.
    ///         <c>Descriptions</c> is the whole map, which is what an edit screen needs — and it was
    ///         published on every read carrying <c>null</c>, always: declared <c>[MapIgnore]</c>, filled
    ///         by nobody, and its own comment claiming to demonstrate full access to the type.
    ///     </para>
    ///     <para>
    ///         ⚠️ A field that is always null is indistinguishable from a row with no translations, which
    ///         is why nothing ever failed.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheRead_CarriesEveryTranslation_NotOnlyTheCurrentOne()
    {
        var id = await APropertyDescribedAsync(new { en = "A quiet hotel", it = "Un albergo tranquillo" });

        var read = await GetAsync<JsonElement>($"/api/properties/{id}");

        var all = read.GetProperty("descriptions");
        all.ValueKind.Should().NotBe(JsonValueKind.Null,
            "the map is what an edit screen reads, and it was published empty on every response");
        all.GetProperty("en").GetString().Should().Be("A quiet hotel");
        all.GetProperty("it").GetString().Should().Be("Un albergo tranquillo");
    }

    /// <summary>
    ///     And the <b>SQL projection</b> carries it too, which is the half nobody had measured.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>PropertyDetailDto</c> declares <c>[GenerateProjection]</c>, and the column is JSON behind
    ///         a value converter — so whether a localized field survives translation to SQL was the open
    ///         question that kept this field <c>[MapIgnore]</c>. The read endpoint uses <c>FromEntity</c>,
    ///         so no route exercises the projection: this case runs it directly.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>IgnoreQueryFilters</c> because outside a request there is no ambient tenant and
    ///         <c>Property</c> is an <c>ITenantEntity</c>. The subject is the projection, not the filters.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheProjection_CarriesTheLocalizedFieldToo()
    {
        var id = await APropertyDescribedAsync(new { en = "By the sea", it = "Sul mare" });

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        var dto = await db.Set<Property>()
            .IgnoreQueryFilters()
            .Where(p => p.PersistenceId == id)
            .Select(PropertyDetailDto.Projection)
            .SingleAsync();

        dto.Descriptions.Should().NotBeNull("the projection reads the JSON column through its converter");
        dto.Descriptions!.GetExact("en").Should().Be("By the sea");
        dto.Descriptions.GetExact("it").Should().Be("Sul mare");
    }
}
