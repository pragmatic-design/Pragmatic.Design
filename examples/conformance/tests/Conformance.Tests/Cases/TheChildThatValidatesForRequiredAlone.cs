using System.Net;
using Conformance.Tests.Infrastructure;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A child that validates only through C#'s <c>required</c> is checked like one with attributes.
/// </summary>
/// <remarks>
///     <para>
///         <c>WriteTagNoteMutation.Text</c> is <c>required</c> and carries no attributes. The validator
///         generator emits a <c>Validate()</c> for it too — the presence guard — so
///         <c>ValidateNestedTree()</c> must decide whether to call it by the same criterion, not by looking
///         only at the attributes: two answers to the same question would disagree. At the root the invoker
///         calls <c>Validate()</c> anyway; on a nested child, only this rule makes someone check that
///         <c>required</c>.
///     </para>
///     <para>
///         ⚠️ The control case is the second test: the same shape with the text filled must pass. Without it,
///         a validation that refused every note would be indistinguishable from one that checks presence.
///     </para>
/// </remarks>
public class TheChildThatValidatesForRequiredAlone(PostgresFixture fixture) : E2ETestBase(fixture)
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

    private static object[] LinesWithANote(string text) =>
    [
        new
        {
            id = Guid.NewGuid(),
            product = "Widget",
            quantity = 1,
            allocations = new[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    warehouse = "milan",
                    quantity = 1,
                    slot = new { aisle = "A", shelf = 3 },
                    tags = new[]
                    {
                        new
                        {
                            id = Guid.NewGuid(),
                            label = "fragile",
                            notes = new[] { new { id = Guid.NewGuid(), text } },
                        },
                    },
                },
            },
        },
    ];

    [Fact]
    public async Task ARequiredOnTheFourthLevel_IsVerified()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/lines", LinesWithANote(text: ""));
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422 — the leaf's required must be checked — received {(int)response.StatusCode} {text}");
        Assert.Contains("validation.required", text);
    }

    [Fact]
    public async Task TheControl_TheSameShapeWithAText_IsWritten()
    {
        var orderId = await AnOrderAsync();

        var response = await PutAsync($"/api/orders/{orderId}/lines", LinesWithANote(text: "fragile"));

        response.EnsureSuccessStatusCode();
    }
}
