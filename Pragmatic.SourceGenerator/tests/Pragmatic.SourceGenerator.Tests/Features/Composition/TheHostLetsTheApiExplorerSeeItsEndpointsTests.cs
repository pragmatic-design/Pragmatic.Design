using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     A host registers what lets the runtime OpenAPI document describe the generated endpoints, and
///     takes them out of it when <c>EnableOpenApi</c> is off.
/// </summary>
/// <remarks>
///     The library-mode registration is proved by an executed document in
///     <c>Pragmatic.Integration.Tests.TheRuntimeDocumentTests</c>. A host does not call that registration:
///     it has its own, written by a different template, and a line added to one of the two is the
///     line the other one lacks.
/// </remarks>
public class TheHostLetsTheApiExplorerSeeItsEndpointsTests
{
    [Fact]
    public void TheHost_RegistersTheDescriptionProvider()
    {
        var services = HostWithOneModuleEndpoint.ServicesOf(HostWithOneModuleEndpoint.Run(anonymous: false));

        var start = services.IndexOf("RegisterAllEndpoints(this IServiceCollection services)", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the host registers the endpoint services it discovered");

        services.Substring(start).Should().Contain(
            "global::Pragmatic.Endpoints.ApiExplorer.PragmaticApiDescriptionExtensions.AddPragmaticApiDescriptions(services);");
    }

    [Fact]
    public void TheHostsRoot_IsTakenOutOfTheDocument_WhenEnableOpenApiIsOff()
    {
        var mapping = HostWithOneModuleEndpoint.MapAllEndpointsOf(HostWithOneModuleEndpoint.Run(anonymous: false));

        mapping.Should().Contain("if (!pragmaticOptions.EnableOpenApi)");
        mapping.Should().Contain(
            "global::Microsoft.AspNetCore.Http.OpenApiRouteHandlerBuilderExtensions.ExcludeFromDescription(root);",
            "on the root the generated endpoints hang from, not on the application's own");
    }
}
