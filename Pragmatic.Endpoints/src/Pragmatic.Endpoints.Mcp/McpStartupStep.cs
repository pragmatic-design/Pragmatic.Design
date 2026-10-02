using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     Maps the MCP server endpoint and resolves the self-call base address.
///     Order 60 — after routing (50). The MCP endpoint requires authorization by default
///     (<see cref="McpOptions.RequireAuthorization" />); opt out for a trusted-gateway deployment.
/// </summary>
public sealed class McpStartupStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 60;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<McpOptions>();
        var holder = app.ApplicationServices.GetRequiredService<McpSelfAddressHolder>();

        var serverAddress = app.ServerFeatures.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        holder.Address = options.SelfBaseAddress
                         ?? (serverAddress is not null ? new Uri(serverAddress.Replace("*", "localhost")) : holder.Address);

        if (app is not IEndpointRouteBuilder endpoints)
            return;

        var mcp = endpoints.MapMcp(options.Path);

        // Secure by default: the MCP surface is authenticated like any other endpoint.
        if (options.RequireAuthorization)
        {
            if (options.AuthorizationPolicy is { } policy)
                mcp.RequireAuthorization(policy);
            else
                mcp.RequireAuthorization();
        }
    }
}
