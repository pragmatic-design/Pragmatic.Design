using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Composition;
using Pragmatic.Migrations.Configuration;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Migrations.Runner;

namespace Pragmatic.Migrations.Core.Tests.Unit.Configuration;

/// <summary>
///     Guards the migration host configuration defaults — most importantly that breaking
///     changes are NOT force-applied unless the developer explicitly opts in.
/// </summary>
public class MigrationsBuilderTests
{
    private static MigrationOptions BuildOptions(Action<MigrationsBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        var builder = new PragmaticBuilderMock();
        builder.Services.Returns(services);

        if (configure is null)
            builder.UsePragmaticMigrations();
        else
            builder.UsePragmaticMigrations(configure);

        return services.BuildServiceProvider().GetRequiredService<MigrationOptions>();
    }

    [Fact]
    public void UsePragmaticMigrations_ByDefault_DoesNotForceBreakingChanges()
    {
        var options = BuildOptions();

        options.Force.Should().BeFalse("breaking changes must be blocked unless explicitly forced");
        options.DryRun.Should().BeFalse();
    }

    [Fact]
    public void Force_OptsIntoBreakingChanges()
    {
        var options = BuildOptions(m => m.Force());

        options.Force.Should().BeTrue();
    }

    [Fact]
    public void DryRun_EnablesDryRunMode()
    {
        var options = BuildOptions(m => m.DryRun());

        options.DryRun.Should().BeTrue();
    }
}
