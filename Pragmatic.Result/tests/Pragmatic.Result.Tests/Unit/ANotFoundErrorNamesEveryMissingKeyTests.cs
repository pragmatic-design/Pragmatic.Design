using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     <c>NotFoundError.ForAll</c>: one 404 naming every key of a list that names no row.
/// </summary>
/// <remarks>
///     A list of keys with several missing had two answers, both wrong: the first missing key
///     alone, so the caller fixes one and meets the next; or a 404 naming nothing.
/// </remarks>
public class ANotFoundErrorNamesEveryMissingKeyTests
{
    private static readonly Guid First = Guid.Parse("0197a3b0-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("0197a3b0-0000-7000-8000-000000000002");

    [Fact]
    public void ForAll_NamesEveryKey_InTheOrderGiven()
    {
        var error = NotFoundError.ForAll("Employee", [Second, First]);

        error.EntityType.Should().Be("Employee");
        error.EntityId.Should().Be($"{Second}, {First}");
        error.StatusCode.Should().Be(404);
    }

    /// <summary>The control: one missing key is the error the single form gives.</summary>
    [Fact]
    public void ForAll_WithOneKey_IsTheSingleFormsError()
    {
        NotFoundError.ForAll("Employee", [First]).Should().Be(NotFoundError.For("Employee", First));
    }
}
