using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

public class HandlerPipelineTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_ShouldBeHandlerPipeline()
    {
        var model = BuildModel("OrderPlacedHandler");
        var artifact = new HandlerPipelineTemplate(model).RenderOutput();

        // Namespace-qualified so two handlers with the same simple name in different namespaces don't collide.
        artifact.HintName.Should().Be("TestApp.Handlers.OrderPlacedHandler.Pipeline.g.cs");
    }

    [Fact]
    public void RenderOutput_ShouldGeneratePartialClass()
    {
        var model = BuildModel("OrderPlacedHandler");
        var source = new HandlerPipelineTemplate(model).RenderOutput().Text;

        // Outer partial wraps the user's handler; nested sealed Pipeline contains the generated wrapper
        source.Should().Contain("partial class OrderPlacedHandler");
        source.Should().Contain("class Pipeline");
        source.Should().Contain("sealed");
        source.Should().Contain("partial");
    }

    [Fact]
    public void RenderOutput_ShouldContainTelemetry()
    {
        var model = BuildModel("OrderPlacedHandler");
        var source = new HandlerPipelineTemplate(model).RenderOutput().Text;

        source.Should().Contain("MessagingDiagnostics.ActivitySource.StartActivity");
        source.Should().Contain("MessagingDiagnostics.HandlerDuration.Record");
    }

    [Fact]
    public void RenderOutput_WithTimeout_ShouldGenerateCancellationTokenSource()
    {
        var model = BuildModel("SlowHandler") with { HasTimeout = true, TimeoutSeconds = 30 };
        var source = new HandlerPipelineTemplate(model).RenderOutput().Text;

        source.Should().Contain("CancellationTokenSource.CreateLinkedTokenSource");
        source.Should().Contain("CancelAfter");
        source.Should().Contain("30");
    }

    [Fact]
    public void RenderOutput_WithAuthorization_ShouldGenerateCallContextBypass()
    {
        var model = BuildModel("SecureHandler") with { FeatureHasAuthorization = true };
        var source = new HandlerPipelineTemplate(model).RenderOutput().Text;

        source.Should().Contain("ICallContext");
        source.Should().Contain("EnterInternalCall");
    }

    [Fact]
    public void RenderOutput_WithoutAuthorization_ShouldNotHaveCallContext()
    {
        var model = BuildModel("SimpleHandler") with { FeatureHasAuthorization = false };
        var source = new HandlerPipelineTemplate(model).RenderOutput().Text;

        source.Should().NotContain("ICallContext");
    }

    [Fact]
    public void RenderOutput_ShouldContainLoggerMessages()
    {
        var model = BuildModel("MyHandler");
        var source = new HandlerPipelineTemplate(model).RenderOutput().Text;

        // Written out with a body: the logging generator never sees this output, so a
        // [LoggerMessage] partial would stay bodiless and every call to it would be compiled away.
        source.Should().NotContain("[LoggerMessage");
        source.Should().Contain("LoggerMessage.Define<");
        source.Should().Contain("LogHandlerStarted");
        source.Should().Contain("LogHandlerCompleted");
        source.Should().Contain("LogHandlerFailed");
    }

    private static MessageHandlerModel BuildModel(string name) => new()
    {
        Namespace = "TestApp.Handlers",
        TypeName = name,
        Accessibility = "public",
        TypeKind = "class",
        IsPartial = true,
        MessageTypeFqn = "global::TestApp.Events.TestEvent",
        MessageTypeShortName = "TestEvent",
    };
}
