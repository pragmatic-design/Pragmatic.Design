#pragma warning disable CA2007

using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Cli.Discovery;

namespace Pragmatic.Migrations.Core.Tests.Unit.Cli;

public class ConnectionStringResolverTests
{
    [Fact]
    public void Resolve_WithOverride_ReturnsOverride()
    {
        var resolver = new ConnectionStringResolver(null, "Host=override");
        resolver.Resolve("AnyDb").Should().Be("Host=override");
    }

    [Fact]
    public void Resolve_WithOverride_IgnoresConfigKey()
    {
        var resolver = new ConnectionStringResolver(null, "Host=override");
        resolver.Resolve("AnyDb", "ConnectionStrings:App").Should().Be("Host=override");
    }

    [Fact]
    public void Resolve_WithoutConfig_ReturnsNull()
    {
        var resolver = new ConnectionStringResolver("/nonexistent/path.json", null);
        resolver.Resolve("AnyDb").Should().BeNull();
    }
}
