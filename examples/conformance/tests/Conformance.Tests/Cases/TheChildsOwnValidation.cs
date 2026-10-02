using System.Net;
using Conformance.Tests.Infrastructure;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A nested child carries its own rules, and they are executed.
/// </summary>
/// <remarks>
///     <para>
///         <c>WriteOrderLineMutation.Quantity</c> declares <c>[GreaterThan(0)]</c>. The child arrives inside
///         the parent, not from its own endpoint — it has none — so the only occasion to check it is the
///         parent's pipeline.
///     </para>
///     <para>
///         ⚠️ Two silences are possible on this path. <c>MutationInvoker</c> asks the mutation for
///         <c>ValidateNestedSync()</c>, so the template implementing it must be emitted for mutations, not
///         only for <c>DomainAction</c>s; and it must validate the elements of <c>Lines</c>, because
///         <c>if (Lines is ISyncValidator)</c> is never true for a <c>List&lt;T&gt;</c>.
///     </para>
///     <para>
///         ⚠️ The control case is the second test: the same body with a valid quantity must still pass.
///         Without it, validation applied <b>too much</b> — refusing every nested write — would be
///         indistinguishable from one that works.
///     </para>
/// </remarks>
public class TheChildsOwnValidation(PostgresFixture fixture) : E2ETestBase(fixture)
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
    public async Task AChildThatBreaksItsOwnRule_IsRejected()
    {
        var orderId = await AnOrderAsync();

        // ⚠️ The body is the bare array: the mutation has a single body property, and the convention puts
        // it on the wire without a wrapper.
        var response = await PutAsync($"/api/orders/{orderId}/lines", new[]
        {
            new { id = Guid.NewGuid(), product = "Widget", quantity = 0, allocations = Array.Empty<object>() },
        });

        // 422: the value arrived and it is wrong — it is not a binding failure.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task TheControl_AValidChild_IsStillWritten()
    {
        var orderId = await AnOrderAsync();

        // ⚠️ The body is the bare array: the mutation has a single body property, and the convention puts
        // it on the wire without a wrapper.
        var response = await PutAsync($"/api/orders/{orderId}/lines", new[]
        {
            new { id = Guid.NewGuid(), product = "Widget", quantity = 1, allocations = Array.Empty<object>() },
        });

        response.EnsureSuccessStatusCode();
    }
}
