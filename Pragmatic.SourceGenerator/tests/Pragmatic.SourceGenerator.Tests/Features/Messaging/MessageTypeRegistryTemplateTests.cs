using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

public class MessageTypeRegistryTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_ShouldFollowConvention()
    {
        var types = ImmutableArray.Create(
            new MessageTypeModel { Fqn = "global::App.Events.OrderPlaced", ShortName = "OrderPlaced" });

        var artifact = new MessageTypeRegistryTemplate(types).RenderOutput();

        artifact.HintName.Should().Be("_Infra.Messaging.TypeRegistry.g.cs");
    }

    [Fact]
    public void RenderOutput_ShouldGenerateSwitchExpression()
    {
        var types = ImmutableArray.Create(
            new MessageTypeModel { Fqn = "global::App.Events.OrderPlaced", ShortName = "OrderPlaced" },
            new MessageTypeModel { Fqn = "global::App.Events.InvoicePaid", ShortName = "InvoicePaid" });

        var source = new MessageTypeRegistryTemplate(types).RenderOutput().Text;

        source.Should().Contain("fullyQualifiedTypeName switch");
        source.Should().Contain("global::App.Events.OrderPlaced");
        source.Should().Contain("global::App.Events.InvoicePaid");
        source.Should().Contain("=> null"); // default case
    }

    [Fact]
    public void RenderOutput_ShouldImplementIMessageTypeRegistry()
    {
        var types = ImmutableArray.Create(
            new MessageTypeModel { Fqn = "global::App.OrderPlaced", ShortName = "OrderPlaced" });

        var source = new MessageTypeRegistryTemplate(types).RenderOutput().Text;

        source.Should().Contain("IMessageTypeRegistry");
        source.Should().Contain("Deserialize");
    }

    [Fact]
    public void RenderOutput_ShouldUseJsonSerializer()
    {
        var types = ImmutableArray.Create(
            new MessageTypeModel { Fqn = "global::App.Evt", ShortName = "Evt" });

        var source = new MessageTypeRegistryTemplate(types).RenderOutput().Text;

        // AOT-clean: JsonTypeInfo-based overload resolved from the seam, not the reflection generic overload.
        source.Should().Contain("JsonSerializer.Deserialize(json, (JsonTypeInfo<")
            .And.Contain("_options.GetTypeInfo(typeof(");
    }
}
