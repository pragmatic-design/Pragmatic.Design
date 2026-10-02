using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A GET parameter the caller does not send is worth its C# initializer, not <c>default</c>.
/// </summary>
/// <remarks>
///     <para>
///         The generated binding starts from the instance as the declaration builds it and overwrites only
///         what the query string carries. ⚠️ Starting from <c>default</c> for every scalar would make
///         <c>Page = 1, PageSize = 20</c> arrive as 0 and 0, and a <c>WithPaging(0, 0)</c> would return an
///         empty result without saying why.
///     </para>
///     <para>
///         The control is the parameter that is sent: if that one came back 1 too, the binding would not read
///         the query string at all and the first case would pass for the wrong reason.
///     </para>
/// </remarks>
public class TheInitializersOfAGet(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task AParameterLeftOut_KeepsItsInitializer()
    {
        var echo = await ReadAsync(await Client.GetAsync("/api/paging-defaults"));

        echo.GetProperty("page").GetInt32().Should().Be(1, "it is the declared initializer");
        echo.GetProperty("pageSize").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task TheControl_AParameterSent_Wins()
    {
        var echo = await ReadAsync(await Client.GetAsync("/api/paging-defaults?page=3"));

        echo.GetProperty("page").GetInt32().Should().Be(3, "the query string overwrites the initializer");
        echo.GetProperty("pageSize").GetInt32().Should().Be(20, "and only what it carries");
    }
}
