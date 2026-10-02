using System.Text.Json;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     An aggregate that <b>counts</b> its children.
/// </summary>
/// <remarks>
///     <para>
///         <c>[RollUp&lt;OrderLine&gt;("", RollUpAggregation.Count)]</c> on <c>Order.LineCount</c> is the
///         repository's <c>Count</c>, and a <c>[RollUp]</c> outside the inventory corpus. That the generator
///         produces it and that it compiles is measured in <c>RollUpCountGeneratorTests</c>; here what the
///         application does is measured.
///     </para>
///     <para>
///         ⚠️ The rules must reach the interceptor through the host: the module publishes the
///         <c>RollUpRules</c> category and the host calls what it finds there. Registered only by
///         <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c> — a per-assembly entry point the
///         Pragmatic host path does not call — the interceptor would be mounted with an empty list, and
///         every <c>[RollUp]</c> declared in a generated application would be inert, <c>Sum</c> and
///         <c>Count</c> alike.
///     </para>
/// </remarks>
public class TheCountedChildren(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<JsonElement> AnOrderWithTwoLinesAsync()
        => await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[]
            {
                new { product = "bread", quantity = 2 },
                new { product = "milk", quantity = 1 },
            },
        }));

    private async Task<int> LineCountAsync(Guid id)
        => (await ReadAsync(await Client.GetAsync($"api/orders/{id}/raw")))
            .GetProperty("lineCount").GetInt32();

    /// <summary>The aggregate is published and readable: the column exists and the read carries it.</summary>
    /// <remarks>
    ///     It is the half that makes the one below measurable. Without it «the count is two» cannot be told
    ///     from «the property is not there», and they are two different defects.
    /// </remarks>
    [Fact]
    public async Task TheAggregate_IsPartOfTheEntityAndIsRead()
    {
        var created = await AnOrderWithTwoLinesAsync();
        var id = created.GetProperty("id").GetGuid();

        created.GetProperty("lines").GetArrayLength().Should().Be(2,
            "the two lines were written — it is what the count counts");

        var read = await ReadAsync(await Client.GetAsync($"api/orders/{id}/raw"));
        read.TryGetProperty("lineCount", out _).Should().BeTrue(
            "the aggregate property is mapped and serialized like any other scalar");
    }

    /// <summary>The count follows the writes: plus one on insert, minus one on delete.</summary>
    /// <remarks>
    ///     Measured in both directions because a missing delta and a delta with the wrong sign leave
    ///     different traces.
    /// </remarks>
    [Fact]
    public async Task TheCountFollowsTheWrites_InBothDirections()
    {
        var created = await AnOrderWithTwoLinesAsync();
        var id = created.GetProperty("id").GetGuid();
        var kept = created.GetProperty("lines").EnumerateArray().First();

        (await LineCountAsync(id)).Should().Be(2,
            "two children written, two counted: the interceptor received the Count rule");

        (await PutAsync($"/api/orders/{id}", new
        {
            reference = created.GetProperty("reference").GetString(),
            lines = new[]
            {
                new
                {
                    id = kept.GetProperty("id").GetGuid(),
                    product = kept.GetProperty("product").GetString()!,
                    quantity = kept.GetProperty("quantity").GetInt32(),
                },
            },
        })).EnsureSuccessStatusCode();

        (await LineCountAsync(id)).Should().Be(1,
            "one line removed, one less — the delta has the right sign too");
    }
}
