using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Messaging;

/// <summary>
///     The four messaging attributes no example writes, proved from the <b>source</b>.
/// </summary>
/// <remarks>
///     <para>
///         These attributes stay undeclared in the reference applications and are proved at unit level.
///         The template tests do not prove them: every template case builds the model by hand —
///         <c>BusName = "analytics"</c>, <c>HasBatchProgress = true</c> — so what is asserted is "given
///         this model, the template emits that", and never "given this attribute, the model says that".
///     </para>
///     <para>
///         ⚠️ Renaming the attribute the transform looks for — in <c>MessageHandlerTransform.cs</c>, a
///         fully qualified name in a string — does <b>not</b> go unnoticed: the <c>attributes nothing
///         reads</c> ratchet lists <c>OnBusAttribute</c> and the gate stops before the suites run. This
///         file does not duplicate that defence.
///     </para>
///     <para>
///         What it catches is the variant that ratchet cannot see: the attribute still <b>read</b> and
///         its value dropped. Breaking the line that reads the value — so <c>busName</c> is always
///         null — leaves the ratchet green, leaves <c>BusResolverTemplateTests</c>,
///         <c>HandlerRegistrationTemplateTests</c>, <c>TheSubscriptionSaysWhichModuleIsListeningTests</c>
///         and the runtime <c>MultiBusIsolationTests</c> all green, and fails exactly one case:
///         <see cref="OnBus_RoutesTheSubscriptionAndReplacesTheResolver" />. Every one of those four
///         builds its model by hand, so none of them can tell a read value from a lost one.
///     </para>
///     <para>
///         Each case here writes the attribute in source and asserts the generated file changed, with
///         the control that the same declaration <b>without</b> it does not. The control is what makes
///         the assertion mean anything: "the generated file contains X" is satisfied by a generator
///         that always emits X.
///     </para>
/// </remarks>
public class TheAttributeIsWhatReachesTheGeneratedFileTests
{
    [Fact]
    public void OnBus_RoutesTheSubscriptionAndReplacesTheResolver()
    {
        var withAttribute = Registration("""
            [MessageHandler]
            [OnBus("analytics")]
            public partial class PageViewedHandler : IMessageHandler<PageViewed>
            {
                public Task HandleAsync(PageViewed message, MessageContext context, CancellationToken ct = default)
                    => Task.CompletedTask;
            }
            """);

        withAttribute.Should().Contain("AddMessageSubscriptionOnBus",
            "the subscription has to name the bus, or the consumer of the named transport never binds it");
        withAttribute.Should().Contain("analytics");
        withAttribute.Should().Contain("PragmaticBusResolver.Instance",
            "without replacing DefaultBusResolver the routing decision is read from nothing");

        var without = Registration("""
            [MessageHandler]
            public partial class PageViewedHandler : IMessageHandler<PageViewed>
            {
                public Task HandleAsync(PageViewed message, MessageContext context, CancellationToken ct = default)
                    => Task.CompletedTask;
            }
            """);

        without.Should().NotContain("AddMessageSubscriptionOnBus",
            "the control: a handler with no [OnBus] subscribes on the default bus");
        without.Should().NotContain("IBusResolver",
            "and leaves the default resolver alone — otherwise the case above is satisfied by a "
            + "generator that always replaces it");
    }

    [Fact]
    public void RequestHandler_RegistersTheRequestHandlerItself()
    {
        var withAttribute = Registration("""
            [RequestHandler]
            public partial class GetPageHandler : IRequestHandler<GetPage, PageView>
            {
                public Task<PageView> HandleAsync(GetPage request, CancellationToken ct = default)
                    => Task.FromResult(new PageView());
            }
            """);

        withAttribute.Should().Contain("IRequestHandler<global::Probe.GetPage, global::Probe.PageView>",
            "an answer inside the caller's request needs the handler in the container, and this is "
            + "the line that puts it there");

        var without = Registration("""
            public partial class GetPageHandler : IRequestHandler<GetPage, PageView>
            {
                public Task<PageView> HandleAsync(GetPage request, CancellationToken ct = default)
                    => Task.FromResult(new PageView());
            }
            """);

        without.Should().NotContain("IRequestHandler<",
            "the control: implementing the interface is not the declaration — the attribute is, and a "
            + "generator that registered every implementation would make it meaningless");
    }

    [Fact]
    public void MessageMiddleware_RegistersTheMiddlewareInThePipeline()
    {
        var withAttribute = Registration("""
            [MessageMiddleware]
            public partial class AuditMiddleware : IMessageMiddleware
            {
                public int Order => 10;

                public Task InvokeAsync<TMessage>(TMessage message, MessageContext context,
                    Func<Task> next, CancellationToken ct = default) where TMessage : class => next();
            }
            """);

        withAttribute.Should().Contain("IMessageMiddleware, global::Probe.AuditMiddleware",
            "the pipeline resolves IEnumerable<IMessageMiddleware>, so a middleware that is not "
            + "registered is a per-message concern the application declared and never runs");

        var without = Registration("""
            public partial class AuditMiddleware : IMessageMiddleware
            {
                public int Order => 10;

                public Task InvokeAsync<TMessage>(TMessage message, MessageContext context,
                    Func<Task> next, CancellationToken ct = default) where TMessage : class => next();
            }
            """);

        without.Should().NotContain("global::Probe.AuditMiddleware",
            "the control: the interface alone registers nothing, which is what makes the attribute "
            + "the declaration");
    }

    private static string Registration(string declarations)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Messaging;
            using Pragmatic.Messaging.Attributes;

            namespace Probe;

            public sealed record PageViewed(string Path);

            public sealed class GetPage { public string Path { get; set; } = ""; }

            public sealed class PageView { public string Title { get; set; } = ""; }

            {{declarations}}
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Messaging.IMessageBus)),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Events.IDomainEvent)));

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var registration = sources.FirstOrDefault(s => s.Key.Contains("Messaging.Registration", StringComparison.Ordinal));

        return registration.Value ?? string.Join("\n", sources.Values);
    }
}
