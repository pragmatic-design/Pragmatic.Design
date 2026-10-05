using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     An entry logged inside a scope comes out of the JSON providers as a line that parses, with the scope
///     properties readable under <c>@scopes</c>.
/// </summary>
/// <remarks>
///     The writers run with <c>SkipValidation = true</c> in production, so <c>Utf8JsonWriter</c> lets a
///     malformed write through instead of throwing. Here validation is on, so an invalid write fails the
///     test even before the line is parsed.
/// </remarks>
public class ScopesWriteValidJsonTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pragmatic-scopes-{Guid.NewGuid():N}");

    public ScopesWriteValidJsonTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void JsonProvider_OneScope_WritesTheScopePropertiesAsAnObject()
    {
        using var line = JsonProviderLine(logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "req-1", ["Tenant"] = 3 }))
                logger.LogInformation("Handled {Path}", "/orders");
        });

        var scopes = line.RootElement.GetProperty("@scopes");
        scopes.GetProperty("RequestId").GetString().Should().Be("req-1");
        scopes.GetProperty("Tenant").GetInt32().Should().Be(3);
    }

    [Fact]
    public void JsonProvider_NestedScopes_WritesThePropertiesOfBoth()
    {
        using var line = JsonProviderLine(logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "req-1" }))
            using (logger.BeginScope(new Dictionary<string, object?> { ["Tenant"] = 3 }))
                logger.LogInformation("Handled {Path}", "/orders");
        });

        var scopes = line.RootElement.GetProperty("@scopes");
        scopes.GetProperty("RequestId").GetString().Should().Be("req-1");
        scopes.GetProperty("Tenant").GetInt32().Should().Be(3);
    }

    [Fact]
    public void JsonProvider_AKeyInTwoScopes_WritesItsValuesAsAnArray()
    {
        using var line = JsonProviderLine(logger =>
        {
            using (logger.BeginScope(new Dictionary<string, object?> { ["Step"] = "outer", ["Tenant"] = 3 }))
            using (logger.BeginScope(new Dictionary<string, object?> { ["Step"] = "inner" }))
                logger.LogInformation("Handled {Path}", "/orders");
        });

        var scopes = line.RootElement.GetProperty("@scopes");
        scopes.GetProperty("Tenant").GetInt32().Should().Be(3);

        var steps = scopes.GetProperty("Step");
        steps.ValueKind.Should().Be(JsonValueKind.Array);
        steps.EnumerateArray().Select(step => step.GetString()).Order()
            .Should().BeEquivalentTo(["inner", "outer"]);
    }

    [Fact]
    public void EnhancedJsonProvider_NestedScopesWithARepeatedKey_WritesALineThatParses()
    {
        var config = new PragmaticProviderConfiguration { MinimumLevel = LogLevel.Information, IncludeContextEnrichment = false };
        config.CustomProperties["EnableNDJSON"] = true;
        config.CustomProperties["SkipValidation"] = false;
        var path = Path.Combine(_directory, "log.ndjson");

        using (var provider = new PragmaticEnhancedJsonProvider("enhanced", config, path))
        {
            var logger = provider.CreateLogger("Scopes");
            using (logger.BeginScope(new Dictionary<string, object?> { ["Step"] = "outer", ["Tenant"] = 3 }))
            using (logger.BeginScope(new Dictionary<string, object?> { ["Step"] = "inner" }))
                logger.LogInformation("Handled {Path}", "/orders");
        }

        var files = Directory.GetFiles(_directory, "*", SearchOption.AllDirectories);
        files.Should().ContainSingle();

        using var line = JsonDocument.Parse(File.ReadAllText(files[0]).Trim());
        line.RootElement.GetProperty("@scopes").ValueKind.Should().Be(JsonValueKind.Array);
    }

    private static JsonDocument JsonProviderLine(Action<ILogger> log)
    {
        var config = PragmaticJsonConfiguration.ForJson();
        config.IncludeContextEnrichment = false;
        config.CustomProperties["SkipValidation"] = false;

        var output = new MemoryStream();
        using (var provider = new PragmaticJsonProvider("json", config, output))
            log(provider.CreateLogger("Scopes"));

        return JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()).Trim());
    }
}
