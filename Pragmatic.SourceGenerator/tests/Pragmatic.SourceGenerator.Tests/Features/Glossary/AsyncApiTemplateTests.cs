using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Glossary.Models;
using Pragmatic.SourceGenerator.Features.Glossary.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Glossary;

/// <summary>
///     The AsyncAPI generator emits PragmaticAsyncApi.Json — an AsyncAPI
///     3.0 document with one channel + message per domain event — as a compile-time constant.
/// </summary>
public class AsyncApiTemplateTests
{
    [Fact]
    public void AsyncApi_EmitsChannelAndMessagePerEvent()
    {
        var events = new[]
        {
            new AsyncApiEventModel { Name = "OrderPlaced" },
            new AsyncApiEventModel { Name = "OrderCancelled" }
        }.ToEquatableArray();

        var source = new AsyncApiTemplate(events, "MyApp").RenderOutput().Text;

        source.Should().Contain("namespace MyApp.Generated;");
        source.Should().Contain("public const string Json");
        source.Should().Contain("3.0.0");
        source.Should().Contain("OrderPlaced");
        source.Should().Contain("OrderCancelled");
        source.Should().Contain("#/components/messages/OrderPlaced");
    }

    [Fact]
    public void AsyncApi_MarksPublicIntegrationEvents()
    {
        var events = new[]
        {
            new AsyncApiEventModel { Name = "OrderPlaced", IsPublic = true },
            new AsyncApiEventModel { Name = "StockAdjusted", IsPublic = false }
        }.ToEquatableArray();

        var source = new AsyncApiTemplate(events, "MyApp").RenderOutput().Text;
        var start = source.IndexOf("@\"", System.StringComparison.Ordinal) + 2;
        var end = source.LastIndexOf("\";", System.StringComparison.Ordinal);
        var json = source.Substring(start, end - start).Replace("\"\"", "\"");

        using var doc = JsonDocument.Parse(json);
        var messages = doc.RootElement.GetProperty("components").GetProperty("messages");
        messages.GetProperty("OrderPlaced").GetProperty("x-pragmatic-public").GetBoolean().Should().BeTrue();
        messages.GetProperty("StockAdjusted").GetProperty("x-pragmatic-public").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void AsyncApi_EmitsPayloadSchemaAndObsoleteFlag()
    {
        var events = new[]
        {
            new AsyncApiEventModel
            {
                Name = "OrderPlaced",
                IsPublic = true,
                IsObsolete = true,
                Properties = new[]
                {
                    new AsyncApiPropertyModel { Name = "OrderId", JsonType = "string" },
                    new AsyncApiPropertyModel { Name = "Total", JsonType = "number" }
                }.ToEquatableArray()
            }
        }.ToEquatableArray();

        var source = new AsyncApiTemplate(events, "MyApp").RenderOutput().Text;
        var start = source.IndexOf("@\"", System.StringComparison.Ordinal) + 2;
        var end = source.LastIndexOf("\";", System.StringComparison.Ordinal);
        var json = source.Substring(start, end - start).Replace("\"\"", "\"");

        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.GetProperty("components").GetProperty("messages").GetProperty("OrderPlaced");
        message.GetProperty("x-pragmatic-obsolete").GetBoolean().Should().BeTrue();
        var properties = message.GetProperty("payload").GetProperty("properties");
        properties.GetProperty("OrderId").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("Total").GetProperty("type").GetString().Should().Be("number");
    }

    [Fact]
    public void AsyncApi_ProducesValidJson()
    {
        var events = new[] { new AsyncApiEventModel { Name = "OrderPlaced" } }.ToEquatableArray();

        // Extract the verbatim Json constant content and verify it parses + has the expected shape.
        var source = new AsyncApiTemplate(events, "MyApp").RenderOutput().Text;
        var start = source.IndexOf("@\"", System.StringComparison.Ordinal) + 2;
        var end = source.LastIndexOf("\";", System.StringComparison.Ordinal);
        var json = source.Substring(start, end - start).Replace("\"\"", "\"");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("asyncapi").GetString().Should().Be("3.0.0");
        // ⚠️ The address is the topic the transport carries the event on, not the channel key: the type's
        // name would send a consumer that followed it to subscribe to nothing. This
        // event has no namespace, so the answer is the router's own for that case — see
        // TheChannelAddressIsTheTopicTheTransportUsesTests, which holds the table.
        doc.RootElement.GetProperty("channels").GetProperty("OrderPlaced").GetProperty("address").GetString()
            .Should().Be(".events");
    }
}
