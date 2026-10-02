using Microsoft.Extensions.Configuration;
using Pragmatic.Composition.Hosting;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     A connection string a database declares is checked before anything uses it, and the failure
///     names what is missing.
/// </summary>
/// <remarks>
///     Measured on a consumer launched outside its folder: the empty value travelled on, and the log
///     read <c>Leader election failed</c>, then <c>Migration leader-completion polling failed 5 times</c>,
///     then — last — <c>The ConnectionString property has not been initialized</c>, with no key named
///     anywhere, although the generator knew it at compile time.
/// </remarks>
public sealed class DeclaredConnectionStringsTests
{
    private const string Key = "ConnectionStrings:App";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_WhenTheKeyHasNoValue_ThrowsNamingTheKeyTheDatabaseAndWhereItLooked(string? value)
    {
        var configuration = Configuration(value);

        var act = () => DeclaredConnectionStrings.Require(configuration, Key, "AppDatabase", @"C:\apps\library");

        var message = act.Should().ThrowExactly<InvalidOperationException>().Which.Message;
        message.Should().Contain(Key);
        message.Should().Contain("AppDatabase");
        message.Should().Contain(@"C:\apps\library");
        message.Should().Contain("ConnectionStrings__App");
    }

    [Fact]
    public void Require_WhenTheKeyHasAValue_DoesNotThrow()
    {
        var configuration = Configuration("Host=localhost;Database=app");

        var act = () => DeclaredConnectionStrings.Require(configuration, Key, "AppDatabase", @"C:\apps\library");

        act.Should().NotThrow();
    }

    private static IConfiguration Configuration(string? value)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(value is null ? [] : new Dictionary<string, string?> { [Key] = value })
            .Build();
}
