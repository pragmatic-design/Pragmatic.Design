using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     The bus resolver must be emitted as a real <c>IBusResolver</c> implementation
///     (instance + singleton), not only a static helper, so DI can consume it.
/// </summary>
public class BusResolverTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_ShouldFollowConvention()
    {
        var handlers = ImmutableArray.Create(BuildHandler("App", "AnalyticsHandler", "analytics"));
        var artifact = new BusResolverTemplate(handlers).RenderOutput();

        artifact.HintName.Should().Be("_Infra.Messaging.BusResolver.g.cs");
    }

    [Fact]
    public void RenderOutput_ImplementsIBusResolver_WithSingletonInstance()
    {
        var handlers = ImmutableArray.Create(BuildHandler("App", "AnalyticsHandler", "analytics"));

        var source = new BusResolverTemplate(handlers).RenderOutput().Text;

        // Implements the interface (DI-consumable) and exposes a shared singleton.
        source.Should().Contain(": global::Pragmatic.Messaging.IBusResolver");
        source.Should().Contain("public static readonly PragmaticBusResolver Instance");
        // Instance member + static overload both present.
        source.Should().Contain("public string? GetBusName(string handlerTypeFqn)");
        source.Should().Contain("public static string? ResolveBusName(string handlerTypeFqn)");
        // Maps the handler FQN to its bus.
        source.Should().Contain("\"App.AnalyticsHandler\" => \"analytics\"");
    }

    private static MessageHandlerModel BuildHandler(string ns, string name, string? busName) => new()
    {
        Namespace = ns,
        TypeName = name,
        Accessibility = "public",
        TypeKind = "class",
        MessageTypeFqn = "global::App.SomeEvent",
        MessageTypeShortName = "SomeEvent",
        BusName = busName,
    };
}
