using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Hosting;
using Pragmatic.Maintenance;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     The step is wired into every generated host, so what it maps is a decision about every host's
///     route table: the 503 middleware always, the admin panel and its SSE feed only on request.
/// </summary>
public class MaintenanceStepTests
{
    [Fact]
    public void ConfigurePipeline_ByDefault_MapsNoAdminEndpoints()
    {
        var app = BuildApp(new MaintenanceModeOptions());

        new MaintenanceStep().ConfigurePipeline(app);

        MappedRoutes(app).Should().BeEmpty();
    }

    [Fact]
    public void ConfigurePipeline_WithAdminEndpointsEnabled_MapsTheAdminGroup()
    {
        var app = BuildApp(new MaintenanceModeOptions
        {
            EnableAdminEndpoints = true,
            AdminPath = "/_ops/maint"
        });

        new MaintenanceStep().ConfigurePipeline(app);

        var routes = MappedRoutes(app);
        routes.Should().Contain(r => r.Contains("_ops/maint"));
        routes.Should().Contain(r => r.Contains("panel"));
        routes.Should().Contain(r => r.Contains("stream"));
    }

    private static WebApplication BuildApp(MaintenanceModeOptions options)
    {
        var builder = WebApplication.CreateSlimBuilder();
        var progressStream = new MigrationProgressStream();

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IMaintenanceMode>(new MaintenanceModeService());
        builder.Services.AddSingleton(progressStream);
        builder.Services.AddSingleton<IMigrationProgressStream>(progressStream);

        return builder.Build();
    }

    private static List<string> MappedRoutes(WebApplication app)
        => ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();
}
