using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Messaging.Models;
using Pragmatic.SourceGenerator.Features.Messaging.Templates;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

public class HandlerRegistrationTemplateTests
{
    [Fact]
    public void RenderOutput_HintName_ShouldFollowConvention()
    {
        var handlers = ImmutableArray.Create(BuildHandler("MyApp.Handlers", "OrderHandler", "global::MyApp.Events.OrderPlaced"));
        var artifact = new HandlerRegistrationTemplate(handlers).RenderOutput();

        artifact.HintName.Should().Be("_Infra.Messaging.Registration.g.cs");
    }

    [Fact]
    public void RenderOutput_SingleHandler_ShouldGenerateRegistration()
    {
        var handlers = ImmutableArray.Create(BuildHandler("MyApp.Handlers", "OrderHandler", "global::MyApp.Events.OrderPlaced"));
        var source = new HandlerRegistrationTemplate(handlers).RenderOutput().Text;

        source.Should().Contain("AddPragmaticMessageHandlers");
        source.Should().Contain("IMessageHandler<global::MyApp.Events.OrderPlaced>");
        source.Should().Contain("global::MyApp.Handlers.OrderHandler");
        source.Should().Contain("AddScoped");
    }

    /// <summary>
    ///     A class marked <c>[MessageMiddleware]</c> is registered, which is the whole of what the
    ///     attribute promises.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A shape diagnostic alone is not a reader: without this registration a middleware declared
    ///     this way compiles, passes its own check, and is never called. Nothing fails when a wrapper is
    ///     missing — the handler runs and only what the middleware was for is absent, which is why the
    ///     gap stays unnoticed when packages register their middleware by hand.
    /// </remarks>
    [Fact]
    public void AMiddleware_IsRegisteredBehindItsInterface()
    {
        var source = new HandlerRegistrationTemplate(
                ImmutableArray<MessageHandlerModel>.Empty,
                middlewares: ImmutableArray.Create(new MessageMiddlewareModel { TypeFqn = "global::App.Logging" }))
            .RenderOutput().Text;

        source.Should().Contain(
            "ServiceDescriptor.Scoped<global::Pragmatic.Messaging.IMessageMiddleware, global::App.Logging>()");
    }

    /// <summary>
    ///     One scoped to a message type is wrapped, with the type written in.
    /// </summary>
    /// <remarks>
    ///     The comparison then happens on a value known at compile time. Reading the attribute back at
    ///     run time would be a <c>GetCustomAttribute</c> on every message — reflective, and a decision
    ///     taken where it is already known.
    /// </remarks>
    [Fact]
    public void AMiddlewareScopedToAMessage_IsWrappedWithThatType()
    {
        var source = new HandlerRegistrationTemplate(
                ImmutableArray<MessageHandlerModel>.Empty,
                middlewares: ImmutableArray.Create(new MessageMiddlewareModel
                {
                    TypeFqn = "global::App.Logging",
                    ForMessageTypeFqn = "global::App.Events.OrderPlaced"
                }))
            .RenderOutput().Text;

        source.Should().Contain("global::Pragmatic.Messaging.MessageTypeScopedMiddleware")
            .And.Contain("typeof(global::App.Events.OrderPlaced)")
            .And.Contain("services.TryAddScoped<global::App.Logging>()",
                "the wrapper resolves the concrete type, so it has to be registered too");
    }

    /// <summary>
    ///     The control: an assembly with no middleware registers none.
    /// </summary>
    /// <remarks>
    ///     Without it, "the middleware is registered" is satisfied by a template that emits the line
    ///     unconditionally — which would put a registration naming a type that does not exist into
    ///     every other assembly.
    /// </remarks>
    [Fact]
    public void WithNoMiddleware_NoneIsRegistered()
    {
        var handlers = ImmutableArray.Create(BuildHandler("App", "Handler1", "global::App.Event1"));

        var source = new HandlerRegistrationTemplate(handlers).RenderOutput().Text;

        source.Should().NotContain("IMessageMiddleware");
    }

    [Fact]
    public void RenderOutput_MultipleHandlers_ShouldRegisterAll()
    {
        var handlers = ImmutableArray.Create(
            BuildHandler("App", "Handler1", "global::App.Event1"),
            BuildHandler("App", "Handler2", "global::App.Event2"));

        var source = new HandlerRegistrationTemplate(handlers).RenderOutput().Text;

        source.Should().Contain("Handler1");
        source.Should().Contain("Handler2");
    }

    [Fact]
    public void RenderOutput_WithRequestHandler_ShouldGenerateRequestHandlerRegistration()
    {
        var handlers = ImmutableArray<MessageHandlerModel>.Empty;
        var requestHandlers = ImmutableArray.Create(BuildRequestHandler(
            "MyApp.Handlers", "PaymentQueryHandler",
            "global::MyApp.Queries.GetPayment", "global::MyApp.Queries.PaymentResult"));

        var source = new HandlerRegistrationTemplate(handlers, requestHandlers).RenderOutput().Text;

        source.Should().Contain("AddPragmaticMessageHandlers");
        source.Should().Contain("IRequestHandler<global::MyApp.Queries.GetPayment, global::MyApp.Queries.PaymentResult>");
        source.Should().Contain("global::MyApp.Handlers.PaymentQueryHandler");
        source.Should().Contain("AddScoped");
    }

    [Fact]
    public void RenderOutput_WithBothHandlerTypes_ShouldRegisterBoth()
    {
        var handlers = ImmutableArray.Create(
            BuildHandler("App", "EventHandler1", "global::App.Event1"));
        var requestHandlers = ImmutableArray.Create(
            BuildRequestHandler("App", "QueryHandler1", "global::App.Query1", "global::App.Result1"));

        var source = new HandlerRegistrationTemplate(handlers, requestHandlers).RenderOutput().Text;

        source.Should().Contain("IMessageHandler<global::App.Event1>");
        source.Should().Contain("IRequestHandler<global::App.Query1, global::App.Result1>");
    }

    [Fact]
    public void RenderOutput_WithOnBusHandler_ReplacesDefaultBusResolver()
    {
        // When a handler declares [OnBus], the registration must replace DefaultBusResolver
        // with the SG-generated PragmaticBusResolver so [OnBus] routing is honored at runtime.
        var handlers = ImmutableArray.Create(
            BuildHandler("App", "AnalyticsHandler", "global::App.Event1", busName: "analytics"));

        var source = new HandlerRegistrationTemplate(handlers).RenderOutput().Text;

        source.Should().Contain("global::Pragmatic.Messaging.IBusResolver");
        source.Should().Contain("global::Pragmatic.Messaging.Generated.PragmaticBusResolver.Instance");
        source.Should().Contain("Replace");
    }

    [Fact]
    public void RenderOutput_WithoutOnBusHandler_DoesNotTouchBusResolver()
    {
        var handlers = ImmutableArray.Create(BuildHandler("App", "PlainHandler", "global::App.Event1"));

        var source = new HandlerRegistrationTemplate(handlers).RenderOutput().Text;

        source.Should().NotContain("IBusResolver");
    }

    [Fact]
    public void Validate_OnlyRequestHandlers_ShouldBeValid()
    {
        var requestHandlers = ImmutableArray.Create(BuildRequestHandler(
            "App", "Handler", "global::App.Req", "global::App.Res"));

        var artifact = new HandlerRegistrationTemplate(
            ImmutableArray<MessageHandlerModel>.Empty, requestHandlers).RenderOutput();

        artifact.IsEmpty.Should().BeFalse();
        artifact.Text.Should().Contain("IRequestHandler");
    }

    private static MessageHandlerModel BuildHandler(
        string ns, string name, string messageTypeFqn, string? busName = null) => new()
    {
        Namespace = ns,
        TypeName = name,
        Accessibility = "public",
        TypeKind = "class",
        MessageTypeFqn = messageTypeFqn,
        MessageTypeShortName = messageTypeFqn.Split('.').Last(),
        BusName = busName,
    };

    private static RequestHandlerModel BuildRequestHandler(
        string ns, string name, string requestTypeFqn, string responseTypeFqn) => new()
    {
        Namespace = ns,
        TypeName = name,
        Accessibility = "public",
        TypeKind = "class",
        RequestTypeFqn = requestTypeFqn,
        ResponseTypeFqn = responseTypeFqn,
    };
}
