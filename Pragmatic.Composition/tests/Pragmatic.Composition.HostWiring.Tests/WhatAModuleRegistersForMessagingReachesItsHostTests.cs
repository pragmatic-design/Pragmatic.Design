// Pragmatic.Composition.HostWiring.Tests - What a module registers for messaging reaches its host

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     Whatever a module's messaging registration holds, the host that includes the module
///     calls it: a request handler or a middleware on its own, not only a message handler.
/// </summary>
/// <remarks>
///     <para>
///         The registration file is written for a request handler (and for a middleware and a
///         partition key), so the metadata that tells the host to call it has to be written for them
///         too, not only for message handlers and declared events. Otherwise a module whose only
///         messaging is a <c>[RequestHandler]</c> has a registration nobody calls: the handler is never
///         in the container, the request queue has no consumer, and every request times out.
///     </para>
/// </remarks>
public sealed class WhatAModuleRegistersForMessagingReachesItsHostTests
{
    private const string Registration = "Catalog.Generated.PragmaticMessageHandlerRegistration.AddPragmaticMessageHandlers";

    private const string RequestHandlerOnly = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Messaging;
        using Pragmatic.Messaging.Attributes;

        namespace Catalog
        {
            [Module(Name = "Catalog")]
            public sealed class CatalogModule;

            [Boundary]
            public partial class CatalogBoundary;

            public sealed record GetPrice(string Sku);

            public sealed record Price(decimal Amount);

            [RequestHandler]
            public sealed partial class AnswerPrices : IRequestHandler<GetPrice, Price>
            {
                public Task<Price> HandleAsync(GetPrice request, MessageContext context, CancellationToken ct = default)
                    => Task.FromResult(new Price(1m));
            }
        }
        """;

    private const string MiddlewareOnly = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Messaging;
        using Pragmatic.Messaging.Attributes;

        namespace Catalog
        {
            [Module(Name = "Catalog")]
            public sealed class CatalogModule;

            [Boundary]
            public partial class CatalogBoundary;

            [MessageMiddleware]
            public sealed partial class TimeEveryMessage : IMessageMiddleware
            {
                public int Order => 10;

                public Task InvokeAsync<T>(T message, MessageContext context,
                    MessageHandlerDelegate next, CancellationToken ct = default) where T : notnull => next();
            }
        }
        """;

    private const string MessageHandler = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Messaging;
        using Pragmatic.Messaging.Attributes;

        namespace Catalog
        {
            [Module(Name = "Catalog")]
            public sealed class CatalogModule;

            [Boundary]
            public partial class CatalogBoundary;

            public sealed record PriceChanged(string Sku);

            [MessageHandler]
            public sealed partial class RecordThePrice : IMessageHandler<PriceChanged>
            {
                public Task HandleAsync(PriceChanged message, MessageContext context, CancellationToken ct = default)
                    => Task.CompletedTask;
            }
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;

        namespace Shop.Host;

        [Module]
        [Include<Catalog.CatalogModule>]
        public sealed class ShopHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void AModuleWhoseOnlyMessagingIsARequestHandler_IsRegisteredByItsHost()
    {
        var (errors, generated) = ModuleAndHost.Generate("Catalog", [RequestHandlerOnly], "Shop.Host", Host);

        errors.Should().BeEmpty();
        generated["Host.Services.g.cs"].Should().Contain(Registration,
            "the handler is registered there and its request queue is bound from there; uncalled, every "
            + "request to it times out");
    }

    [Fact]
    public void AModuleWhoseOnlyMessagingIsAMiddleware_IsRegisteredByItsHost()
    {
        var (errors, generated) = ModuleAndHost.Generate("Catalog", [MiddlewareOnly], "Shop.Host", Host);

        errors.Should().BeEmpty();
        generated["Host.Services.g.cs"].Should().Contain(Registration,
            "a middleware nobody registers wraps nothing");
    }

    /// <summary>The control: a module with a message handler was registered before, and still is.</summary>
    [Fact]
    public void AModuleWithAMessageHandler_IsRegisteredByItsHost()
    {
        var (errors, generated) = ModuleAndHost.Generate("Catalog", [MessageHandler], "Shop.Host", Host);

        errors.Should().BeEmpty();
        generated["Host.Services.g.cs"].Should().Contain(Registration);
    }
}
