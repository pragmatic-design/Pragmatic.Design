using System.Net;
using Conformance.Tests.Infrastructure;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     Which code a successful result returns.
/// </summary>
/// <remarks>
///     <para>
///         «<c>200</c>, or <c>201</c> with <c>[CreatedAt]</c>» is not the rule. The real rule has four
///         branches, and none looks at <c>[CreatedAt]</c>: that provides the <c>Location</c>, not the code.
///         The four cases are here, because a branching rule is verified on all branches or on none.
///     </para>
///     <list type="table">
///         <item><description>void → <c>204</c></description></item>
///         <item><description><c>Create</c> mutation → <c>201</c></description></item>
///         <item><description>mutation of another mode → <c>200</c></description></item>
///         <item><description>non-mutation on <c>POST</c> → <c>201</c>, even without <c>[CreatedAt]</c></description></item>
///     </list>
///     <para>
///         ⚠️ The last one is the surprising one, and the first three are what make it a measure instead of
///         an isolated assertion: if the result translation broke wholesale they would all fall, and that
///         would be known to be another diagnosis.
///     </para>
/// </remarks>
public class TheSuccessStatusCode(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<Guid> AnOrderAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        }));

        return created.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task ACreateMutation_Answers201()
    {
        var response = await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AnUpdateMutation_Answers200()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/reference", new
        {
            id = orderId,
            reference = "ORD-UPDATED-1",
        });

        // An Update exposed as PUT created nothing, and does not claim to.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ANonMutationOnPost_Answers201_WithNoCreatedAtInSight()
    {
        var orderId = await AnOrderAsync();

        // Counts the lines. It creates nothing, carries no [CreatedAt], and gets 201: for an operation that
        // is not a mutation the rule looks only at the verb.
        var response = await PostAsync($"/api/orders/{orderId}/line-count", new { id = orderId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task AResultWithNoValue_Answers204()
    {
        var orderId = await AnOrderAsync();

        // AcknowledgeOrderAction is void, and sits on POST: if the verb decided alone it would be 201.
        var response = await PostAsync($"/api/orders/{orderId}/acknowledge", new { id = orderId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
