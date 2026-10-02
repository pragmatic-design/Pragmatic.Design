using System.Net;
using System.Text.Json;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     1:N at depth 1, through a mutation, on PostgreSQL over HTTP.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: the collection is merged by key, and the invoker loads the existing rows before
///         merging.
///     </para>
///     <para>
///         ⚠️ The difference from <c>Conformance.Poco.Tests</c> is not the transport: there the merge
///         happens on in-memory objects, where «the existing collection» is always the real one. Here it
///         must first be <b>loaded from the database</b>, and if it were not the merge would find nothing
///         to update and would rewrite everything. That step is what these cases prove and those cannot.
///     </para>
/// </remarks>
public class OneToManyDepth1(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>Creation carries the lines with it, in one go.</summary>
    [Fact]
    public async Task Creating_WritesTheChildrenToo()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[]
            {
                new { product = "bread", quantity = 2 },
                new { product = "milk", quantity = 1 },
            },
        }));

        created.GetProperty("lines").GetArrayLength().Should().Be(2,
            "ToEntity builds the whole graph");
    }

    /// <summary>
    ///     ⚠️ The central case: updating the set keeps the identity of what remains.
    /// </summary>
    /// <remarks>
    ///     This is where it shows whether the invoker loaded: without the existing rows in memory, the
    ///     merge would consider every incoming element new and all the ids would change.
    /// </remarks>
    [Fact]
    public async Task Updating_KeepsTheIdentityOfWhatRemains()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[]
            {
                new { product = "bread", quantity = 1 },
                new { product = "milk", quantity = 1 },
            },
        }));

        var orderId = created.GetProperty("id").GetGuid();
        var lines = created.GetProperty("lines").EnumerateArray().ToList();
        var keptId = lines.First(l => l.GetProperty("product").GetString() == "bread")
            .GetProperty("id").GetGuid();

        // ⚠️ The body IS the collection, not an object containing it: the mutation has a single body
        // property — Id comes from the route — and in that case the body is bound directly to it.
        // Sending { "lines": [...] } answers 400.
        var updated = await ReadAsync(await PutAsync($"/api/orders/{orderId}/lines", new[]
        {
            new { id = keptId, product = "bread", quantity = 9 },    // exists
            new { id = Guid.Empty, product = "eggs", quantity = 3 }, // new
        }));

        var after = updated.GetProperty("lines").EnumerateArray().ToList();

        after.Should().HaveCount(2, "one updated, one created, one removed — the Sync strategy");

        var kept = after.Single(l => l.GetProperty("product").GetString() == "bread");
        kept.GetProperty("id").GetGuid().Should().Be(keptId,
            "the row with a matching key keeps its id: it was merged, not rebuilt. "
            + "Had the invoker not loaded the existing rows, this id would have changed");
        kept.GetProperty("quantity").GetInt32().Should().Be(9,
            "and the value arrived: without this assertion a merge that writes nothing would pass");

        after.Should().Contain(l => l.GetProperty("product").GetString() == "eggs",
            "what was not there is created");
        after.Should().NotContain(l => l.GetProperty("product").GetString() == "milk",
            "and what does not arrive goes away");
    }

    /// <summary>
    ///     The independent re-read: the write reached the database, not just the response.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Checking a write by reading the write's own response does not prove that anything was
    ///     persisted. This query is a separate observation.
    /// </remarks>
    [Fact]
    public async Task WhatWasWritten_IsThereOnASeparateRead()
    {
        var reference = $"ORD-{Guid.NewGuid():N}"[..12];

        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference,
            lines = new[] { new { product = "bread", quantity = 2 } },
        }));

        var orderId = created.GetProperty("id").GetGuid();

        var reread = await ReadAsync(await Client.GetAsync($"/api/orders/{orderId}"));

        reread.GetProperty("reference").GetString().Should().Be(reference);
        reread.GetProperty("lines").GetArrayLength().Should().Be(1,
            "and the lines come back filled: the Includes come from OrderDto.RequiredNavigations, "
            + "without the query naming the navigation");
    }

    /// <summary>A read for an id that does not exist answers 404, not 200 with nothing inside.</summary>
    [Fact]
    public async Task ReadingSomethingAbsent_Answers404()
    {
        var response = await Client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "Single = true makes it answer 404 instead of an empty list");
    }
}
