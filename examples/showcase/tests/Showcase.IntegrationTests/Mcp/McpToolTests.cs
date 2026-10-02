using Pragmatic.Testing.Assertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Mcp;

/// <summary>
///     W7 endpoint gap: [McpTool] endpoints exposed over the Model Context Protocol at /mcp.
///     Uses the real MCP client SDK (streamable HTTP) against the running host: ListTools
///     surfaces the manifest-derived schema, CallTool executes through the full endpoint
///     pipeline via self-HTTP (identity headers forwarded).
/// </summary>
public class McpToolTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ListTools_SurfacesMcpToolEndpoints()
    {
        await using var mcpClient = await CreateMcpClientAsync();

        var tools = await mcpClient.ListToolsAsync();

        var names = tools.Select(t => t.Name).ToList();
        names.Should().Contain("booking_getsessionhint");
        names.Should().Contain("booking_createbookingnote");

        var createNote = tools.Single(t => t.Name == "booking_createbookingnote");
        createNote.Description.Should().Contain("booking note");
        createNote.JsonSchema.GetRawText().Should().Contain("\"text\"",
            "the input schema must expose the body properties from the manifest");
    }

    [Fact]
    public async Task CallTool_ExecutesEndpointThroughPipeline()
    {
        await using var mcpClient = await CreateMcpClientAsync();

        var result = await mcpClient.CallToolAsync(
            "booking_createbookingnote",
            new Dictionary<string, object?> { ["text"] = "mcp says hi" });

        result.IsError.Should().BeFalse();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("mcp says hi");
    }

    [Fact]
    public async Task CallTool_GetWithoutArguments_ReturnsPayload()
    {
        await using var mcpClient = await CreateMcpClientAsync();

        var result = await mcpClient.CallToolAsync("booking_getsessionhint");

        var payload = string.Join(" | ", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        result.IsError.Should().BeFalse($"tool call failed with: {payload}");
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("none");
    }

    private async Task<McpClient> CreateMcpClientAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(Client.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        }, Client, ownsHttpClient: false);

        return await McpClient.CreateAsync(transport);
    }
}
