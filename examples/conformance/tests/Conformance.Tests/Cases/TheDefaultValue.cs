using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[DefaultValue]</c> on a string.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Mistranslated, this shape <b>stops the application from starting</b>. Raw in the DDL, the C#
///         literal <c>"pending"</c> in double quotes is an identifier in PostgreSQL:
///         <c>0A000: cannot use column reference in DEFAULT expression</c>, migration refused.
///     </para>
///     <para>
///         A numeric <c>[DefaultValue]</c> hides it, because there the C# and SQL literals happen to
///         coincide. The example documented on the attribute, instead, is <c>[DefaultValue("EUR")]</c>.
///     </para>
///     <para>
///         The most important case is the first, and it is implicit: if the translation went wrong again,
///         the migration would fail at startup and <b>the whole suite</b> would turn red. This file makes
///         explicit what that failure would mean.
///     </para>
/// </remarks>
public class TheDefaultValue(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>The schema is created: the migration with that default is not refused.</summary>
    [Fact]
    public async Task TheSchema_IsCreatedWithAStringDefault()
    {
        var response = await Client.GetAsync("/api/orders/" + Guid.NewGuid());

        ((int)response.StatusCode).Should().BeInRange(400, 499,
            "a domain response means the host started, so the migration with "
            + "[DefaultValue(\"pending\")] was accepted");
    }

    /// <summary>
    ///     ⚠️ And the <b>factory</b> sets the value, not the column.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two independent mechanisms carry the same default, and this case says which of the two worked:
    ///         EF sends an explicit value for every mapped property, so the column default never comes into
    ///         play on an insert of its own. If <c>pending</c> is read here, it is because construction went
    ///         through <c>Create()</c>.
    ///     </para>
    ///     <para>
    ///         An invoker building with <c>new</c> would leave it empty.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheValue_ComesFromTheGeneratedFactory()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[]
            {
                new
                {
                    product = "bread",
                    quantity = 1,
                    allocations = new[]
                    {
                        new
                        {
                            warehouse = "milan",
                            quantity = 1,
                            slot = new { aisle = "A", shelf = 1 },
                            tags = new[] { new { label = "fragile" } },
                        },
                    },
                },
            },
        }));

        var status = created.GetProperty("lines").EnumerateArray().Single()
            .GetProperty("allocations").EnumerateArray().Single()
            .GetProperty("tags").EnumerateArray().Single()
            .GetProperty("status").GetString();

        status.Should().Be("pending",
            "the child is built with the generated factory, which applies [DefaultValue]. With `new` "
            + "it would be the empty string, and no column would put it back: EF always sends an "
            + "explicit value");
    }
}
