using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     E2E for <c>[ReturnsDto&lt;T&gt;]</c> on a partial part of an operation <c>[Resource]</c> scaffolds.
/// </summary>
/// <remarks>
///     <para>
///         <c>ResourceSearchGuestQuery</c> is decorated to answer with the module's own
///         <c>GuestDto</c>; the list endpoint beside it is left alone. So the two run through the same
///         generated pipeline with different shapes, which is what makes the choice per-operation.
///     </para>
///     <para>
///         It has to be executed, not snapshotted. The three defects this scaffolding produced before —
///         a filter on an untranslatable <c>Id</c>, a read DTO with no identifier, an endpoint mapped
///         with no authorization — were all invisible to a test that read the generated text, and all
///         obvious the first time a request reached the database.
///     </para>
/// </remarks>
public class ResourceDeclaredDtoTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string GuestsUrl = "/api/booking/guests";

    /// <summary>
    ///     The declared DTO is what comes back — including a property the scaffolded shape does not have.
    /// </summary>
    [Fact]
    public async Task ResourceSearch_AnswersWithTheDeclaredDto()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await CreateGuest(tag, nationality: "IT");

        var response = await GetRawAsync($"{GuestsUrl}/search?lastName={tag}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the search answered {0}", await response.Content.ReadAsStringAsync());
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("items");

        var mine = items.EnumerateArray().Single();
        mine.GetProperty("nationality").GetString().Should().Be("IT",
            "nationality is projected by GuestDto and absent from the scaffolded GuestListItemDto");
        mine.GetProperty("fullName").GetString().Should().NotBeNullOrEmpty(
            "FullName is GuestDto's own computed member, so it can only come from the declared type");
    }

    /// <summary>
    ///     The filters declared on the partial part are the ones the search accepts, and they filter.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two things at once, because they fail together. The scaffolded search declares a filter per
    ///         text column, and an endpoint that bound none of them would return the whole table on every
    ///         call — green in every snapshot, because the generated text says "Contains" and a snapshot
    ///         does not execute it.
    ///     </para>
    ///     <para>
    ///         And the declaration replaces the convention rather than extending it:
    ///         <c>preferredLanguage</c> is a conventional filter and is not accepted here, because this
    ///         resource's part names two fields and neither is that one.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ResourceSearch_FiltersByTheDeclaredFieldsOnly()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await CreateGuest(tag, nationality: "IT");
        await CreateGuest(tag, nationality: "FR");

        var matching = await GetRawAsync($"{GuestsUrl}/search?lastName={tag}&nationality=IT");
        var byDroppedFilter = await GetRawAsync($"{GuestsUrl}/search?lastName={tag}&preferredLanguage=en");

        var narrowed = (await matching.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("items");
        narrowed.GetArrayLength().Should().Be(1,
            "nationality is declared with Equals, so it selects one of the two guests");
        narrowed.EnumerateArray().Single().GetProperty("nationality").GetString().Should().Be("IT");

        var unfiltered = (await byDroppedFilter.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("items");
        unfiltered.GetArrayLength().Should().Be(2,
            "preferredLanguage is no longer a filter, so naming it narrows nothing");
    }

    /// <summary>
    ///     The list endpoint next to it keeps the scaffolded shape.
    /// </summary>
    /// <remarks>
    ///     The negative half of the test above. Without it, a search answering with the right shape
    ///     would be equally consistent with the DTO having been swapped for the whole resource — and the
    ///     per-operation claim is the whole point of putting the attribute on the operation.
    /// </remarks>
    [Fact]
    public async Task ResourceList_KeepsTheScaffoldedDto()
    {
        await CreateGuest(Guid.NewGuid().ToString("N")[..8], nationality: "FR");

        var response = await GetRawAsync($"{GuestsUrl}?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);

        items.EnumerateArray().First().TryGetProperty("nationality", out _).Should().BeFalse(
            "the list still answers with GuestListItemDto, which carries id and the required scalars only");
    }

    /// <summary>
    ///     A caller without <c>booking.guest.read</c> is refused on both read collections.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two defects met here, and neither showed as a failing test. The permission a developer
    ///         writes on a partial part is a generated constant, which the transform cannot bind; the
    ///         resolution that turns it into a value ran on hand-written endpoints only, so the
    ///         decorated search was mapped with a bare <c>RequireAuthorization()</c>.
    ///     </para>
    ///     <para>
    ///         And the scaffolded list and search asked for nothing at all, while <c>GET /{id}</c> beside
    ///         them asked for <c>booking.guest.read</c> — so the way to read every row was to not ask
    ///         for one.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ReadingManyGuests_WithoutTheReadPermission_IsRefused()
    {
        var client = CreateClientWithPermissions("booking.guest.create");

        var search = await client.GetAsync($"{GuestsUrl}/search?email=nobody");
        var list = await client.GetAsync($"{GuestsUrl}?page=1&pageSize=10");

        search.StatusCode.Should().BeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized],
            "creating a guest does not grant searching them");
        list.StatusCode.Should().BeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized],
            "listing every row is not a lesser right than reading one");
    }

    /// <summary>
    ///     Creates a guest whose last name is the caller's tag, so a search can find exactly these.
    /// </summary>
    private async Task CreateGuest(string tag, string nationality)
    {
        var body = new
        {
            firstName = "Declared",
            lastName = tag,
            email = $"{Guid.NewGuid():N}@test.com",
            nationality,
            preferredLanguage = "en",
        };

        var response = await PostAsync(GuestsUrl, body);
        response.EnsureSuccessStatusCode();
    }
}
