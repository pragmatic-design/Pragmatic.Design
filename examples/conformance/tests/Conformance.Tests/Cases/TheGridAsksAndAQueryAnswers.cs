using System.Net;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A canonical grid request feeds a <b>declared query</b>, not an action.
/// </summary>
/// <remarks>
///     <para>
///         With the bridge available only as an extension on the queryable, the only shape would be a
///         <c>[DomainAction]</c> that takes <c>Query()</c> and applies the request itself. That shape costs
///         more than ergonomics: an action that reads through a repository declares no read — it does not
///         appear in the processing register — and publishes no contract for the fields the grid can name.
///     </para>
///     <para>
///         ⚠️ The bridge is an <b>allow-list</b>: only what carries <c>[Filterable]</c> can be named from
///         the wire. <c>Notes</c> exists on the entity and does not carry it, and it is the control half of
///         the case: without it «the filter applies» would also be satisfied by a bridge that accepts any
///         column name it is given.
///     </para>
/// </remarks>
public class TheGridAsksAndAQueryAnswers(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<string> AnOrderAsync(string reference)
    {
        await ReadAsync(await PostAsync("/api/orders", new
        {
            reference,
            lines = Array.Empty<object>(),
        }));

        return reference;
    }

    /// <summary>The filter the grid sends narrows the rows that come back.</summary>
    [Fact]
    public async Task TheFilterTheGridSends_NarrowsTheRows()
    {
        var tag = $"G{Guid.NewGuid():N}"[..9];
        var wanted = await AnOrderAsync($"{tag}-KEEP");
        await AnOrderAsync($"{tag}-DROP");

        var rows = await ReadAsync(await PostAsync("/api/orders/grid", new
        {
            filters = new[]
            {
                new { field = "Reference", @operator = "Contains", value = $"{tag}-KEEP" },
            },
        }));

        var references = rows.EnumerateArray()
            .Select(r => r.GetProperty("reference").GetString())
            .ToList();

        references.Should().Contain(wanted, "the row the filter names is there");
        references.Should().NotContain($"{tag}-DROP", "and the one it does not name is not");
    }

    /// <summary>And the sort it sends decides the order they come back in.</summary>
    /// <remarks>
    ///     The canonical request carries filters, sorts and page together; the bridge applies all three.
    ///     Without this case «the request arrives» would be satisfied by a bridge that reads only part of
    ///     it.
    /// </remarks>
    [Fact]
    public async Task TheSortTheGridSends_DecidesTheOrder()
    {
        var tag = $"S{Guid.NewGuid():N}"[..9];
        await AnOrderAsync($"{tag}-B");
        await AnOrderAsync($"{tag}-A");

        var rows = await ReadAsync(await PostAsync("/api/orders/grid", new
        {
            filters = new[]
            {
                new { field = "Reference", @operator = "Contains", value = tag },
            },
            sorts = new[]
            {
                new { field = "Reference", direction = "Descending" },
            },
        }));

        var references = rows.EnumerateArray()
            .Select(r => r.GetProperty("reference").GetString())
            .ToList();

        references.Should().HaveCount(2);
        references[0].Should().Be($"{tag}-B", "the descending sort puts B before A");
    }

    /// <summary>
    ///     A field the entity has and does not declare filterable is refused with <b>400</b>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not a 5xx: the request is what is wrong, and the caller fixes it by no longer naming that
    ///         field. The translation lives in <c>UsePragmaticExceptionMapping</c>, which the generated host
    ///         installs, and the body is an <c>application/problem+json</c> like every other framework
    ///         refusal.
    ///     </para>
    ///     <para>
    ///         ⚠️ The detail <b>names the field and does not say why</b>. Unknown and denied come back
    ///         identical on purpose: «you cannot filter on PasswordHash» confirms the column exists, which
    ///         is exactly what a probe looks for. The distinction stays in <c>GridFieldRejection</c> for
    ///         whoever wants it, and out of the response — so the case also asserts what the body does
    ///         <b>not</b> say.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AFieldTheEntityDoesNotDeclareFilterable_IsRefusedWith400()
    {
        var response = await PostAsync("/api/orders/grid", new
        {
            filters = new[]
            {
                new { field = "Notes", @operator = "Contains", value = "anything" },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a field not declared filterable cannot be named from the wire, and the refusal is the " +
            "requester's: silence would return more rows than asked for, a 500 would say the fault is " +
            "the server's");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json",
            "the refusal travels in the framework's error contract, not as a serialized exception");

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("Notes", "the caller must be able to correct the request");
        body.Should().NotContain("Withheld");
        body.Should().NotContain("Unknown",
            "⚠️ the body does not say whether the field does not exist or is denied: the difference is " +
            "what a probe looks for, and telling it in the response would give it away");
    }
}
