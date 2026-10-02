using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     Automatic conversion on the <b>write</b> path.
/// </summary>
/// <remarks>
///     <para>
///         Asserting on the <b>generated text</b> cannot tell a right branch from one nobody takes at
///         runtime, so numeric to string, <c>Guid</c> and <c>bool</c> each need a case on that path.
///     </para>
///     <para>
///         Here the write is <b>executed</b>: four values enter as JSON in the types an HTTP request
///         naturally carries, and are read back in the entity's types. No <c>[MapConverter]</c> says how to
///         go from one to the other.
///     </para>
///     <para>
///         ⚠️ <b>Why the values are not trivial.</b> <c>Quantity = "7"</c> and not <c>"0"</c>,
///         <c>IsPriority = "true"</c> and not <c>"false"</c>, and <c>ExternalId</c> is generated on every
///         run: with the defaults, a conversion never executed and one executed well leave the same row in
///         the database, and the test would pass without touching the mechanism it claims to measure.
///     </para>
/// </remarks>
public class TheConvertedScalars(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task AStringPayload_ReachesTheTypedColumns()
    {
        var externalId = Guid.NewGuid();

        var created = await ReadAsync(await PostAsync("/api/conversion-subjects", new
        {
            externalId = externalId.ToString(),
            isPriority = "true",
            quantity = "7",
            code = 4711,
        }));

        created.GetProperty("externalId").GetGuid().Should().Be(externalId,
            "a string carrying a Guid reaches the Guid column without anyone declaring how");
        created.GetProperty("isPriority").GetBoolean().Should().BeTrue(
            "and the same holds for the bool");
        created.GetProperty("quantity").GetInt32().Should().Be(7,
            "string to numeric, the most common direction");
        created.GetProperty("code").GetString().Should().Be("4711",
            "and the opposite direction — numeric to string");
    }

    /// <summary>The control: a string that does not carry a number.</summary>
    /// <remarks>
    ///     <para>
    ///         Without it, «converts» and «puts the default and carries on» give the same green on the case
    ///         above, because there every value was convertible. The question is whether the failure is
    ///         visible to the caller or silent.
    ///     </para>
    ///     <para>
    ///         ⚠️ The assertion is on the 4xx <b>family</b>, not on a precise code: which of the two — 400 for
    ///         a body that could not be read, 422 for one understood and refused — is another cell's
    ///         decision, and pinning it here would tie this case to it. What this case requires is that it
    ///         is not a 2xx with a zero inside.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AStringThatIsNotANumber_IsRefusedInsteadOfDefaulted()
    {
        var response = await PostAsync("/api/conversion-subjects", new
        {
            externalId = Guid.NewGuid().ToString(),
            isPriority = "true",
            quantity = "seven",
            code = 4711,
        });

        ((int)response.StatusCode).Should().BeInRange(400, 499,
            "a conversion that cannot succeed must say so, not write the default silently");
    }
}
