using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Pragmatic.Endpoints.Mcp;

/// <summary>
///     Executes an MCP tool call by self-calling the corresponding HTTP endpoint —
///     deliberately: the full endpoint pipeline (authorization, validation, tenancy,
///     rate limiting, idempotency) applies to tool invocations exactly like to any client.
///     Identity travels via the forwarded-header allowlist from the incoming MCP request.
/// </summary>
public sealed class McpToolExecutor(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    McpOptions options)
{
    /// <summary>Named HttpClient used for tool self-calls.</summary>
    public const string HttpClientName = "Pragmatic.Mcp.Self";

    /// <summary>Executes the tool with the given JSON arguments; returns (isError, payload).</summary>
    public async Task<(bool IsError, string Payload)> ExecuteAsync(
        McpToolDescriptor tool,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken ct)
    {
        var args = arguments ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        var url = BuildUrl(tool, args);
        using var request = new HttpRequestMessage(HttpMethod.Parse(tool.HttpMethod), url);

        if (tool.BodyProperties.Count > 0)
            request.Content = new StringContent(BuildBody(tool, args), Encoding.UTF8, "application/json");

        // [Idempotent] endpoints require the idempotency header; derive one per invocation.
        if (tool.IdempotencyHeader is { Length: > 0 } idempotencyHeader)
            request.Headers.TryAddWithoutValidation(idempotencyHeader, Guid.NewGuid().ToString("N"));

        ForwardIdentityHeaders(request);

        var client = httpClientFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return (false, payload.Length > 0 ? payload : $"{(int)response.StatusCode} (no content)");

        return (true, payload.Length > 0 ? payload : $"HTTP {(int)response.StatusCode}");
    }

    private static string BuildUrl(McpToolDescriptor tool, IDictionary<string, JsonElement> args)
    {
        var route = tool.FullRoute;
        var query = new List<string>();

        foreach (var parameter in tool.Parameters)
        {
            if (parameter.Name is null) continue;
            if (!TryGetArgument(args, parameter.Name, out var value)) continue;

            if (string.Equals(parameter.In, "path", StringComparison.OrdinalIgnoreCase))
                route = System.Text.RegularExpressions.Regex.Replace(
                    route,
                    $@"\{{{parameter.Name}(:[^}}]*)?\??\}}",
                    Uri.EscapeDataString(value),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            else if (string.Equals(parameter.In, "query", StringComparison.OrdinalIgnoreCase))
                query.Add($"{parameter.Name}={Uri.EscapeDataString(value)}");
        }

        return query.Count > 0 ? route + "?" + string.Join("&", query) : route;
    }

    private static string BuildBody(McpToolDescriptor tool, IDictionary<string, JsonElement> args)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in tool.BodyProperties)
            {
                if (property.Name is null) continue;
                var jsonName = McpJsonSchemaBuilder.CamelCase(property.Name);
                if (args.TryGetValue(jsonName, out var value) ||
                    args.TryGetValue(property.Name, out value))
                {
                    writer.WritePropertyName(jsonName);
                    value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool TryGetArgument(
        IDictionary<string, JsonElement> args, string name, out string value)
    {
        foreach (var (key, element) in args)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? ""
                    : element.GetRawText();
                return true;
            }

        value = "";
        return false;
    }

    private void ForwardIdentityHeaders(HttpRequestMessage request)
    {
        var incoming = httpContextAccessor.HttpContext?.Request.Headers;
        if (incoming is null) return;

        foreach (var header in options.ForwardedHeaders)
            if (incoming.TryGetValue(header, out var values))
                request.Headers.TryAddWithoutValidation(header, (IEnumerable<string?>)values);
    }
}
