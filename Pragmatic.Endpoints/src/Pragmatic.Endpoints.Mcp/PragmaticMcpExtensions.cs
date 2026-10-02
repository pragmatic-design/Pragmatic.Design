using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     Opt-in MCP exposure: <c>services.AddPragmaticMcp()</c> (or <c>UseMcp()</c> on the
///     Pragmatic builder) turns every [McpTool] endpoint into an MCP tool. Tools are listed
///     from the compile-time manifest (low-level handlers — no reflection discovery) and
///     executed via self-HTTP so the full endpoint pipeline applies.
/// </summary>
public static class PragmaticMcpExtensions
{
    /// <summary>Registers the MCP server, the tool catalog/executor, and the pipeline step.</summary>
    public static IServiceCollection AddPragmaticMcp(
        this IServiceCollection services, Action<McpOptions>? configure = null)
    {
        var options = new McpOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<McpSelfAddressHolder>();
        services.AddSingleton<ManifestToolCatalog>();
        services.AddSingleton<McpToolExecutor>();
        services.AddHttpContextAccessor();
        services.AddHttpClient(McpToolExecutor.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
                client.BaseAddress = sp.GetRequiredService<McpSelfAddressHolder>().Address);
        services.AddSingleton<IStartupStep, McpStartupStep>();

        services.AddMcpServer()
            .WithHttpTransport()
            .WithListToolsHandler((context, _) =>
            {
                var catalog = context.Services!.GetRequiredService<ManifestToolCatalog>();
                return ValueTask.FromResult(new ListToolsResult
                {
                    Tools = catalog.Tools.Select(tool => new Tool
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        InputSchema = JsonSerializer.Deserialize<JsonElement>(tool.InputSchemaJson)
                    }).ToList()
                });
            })
            .WithCallToolHandler(async (context, ct) =>
            {
                try
                {
                    var catalog = context.Services!.GetRequiredService<ManifestToolCatalog>();
                    var executor = context.Services!.GetRequiredService<McpToolExecutor>();

                    var name = context.Params?.Name
                               ?? throw new InvalidOperationException("Tool name is required.");
                    var tool = catalog.Find(name)
                               ?? throw new InvalidOperationException($"Unknown tool '{name}'.");

                    var (isError, payload) = await executor
                        .ExecuteAsync(tool, context.Params?.Arguments, ct)
                        .ConfigureAwait(false);

                    return new CallToolResult
                    {
                        IsError = isError,
                        Content = [new TextContentBlock { Text = payload }]
                    };
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Surface the reason instead of the SDK's generic failure message.
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = $"{e.GetType().Name}: {e.Message}" }]
                    };
                }
            });

        return services;
    }
}
