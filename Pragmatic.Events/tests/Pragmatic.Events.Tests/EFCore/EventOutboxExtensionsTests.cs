using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Events.EFCore.Outbox;
using Pragmatic.Events.Tests.Fixtures;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     Wiring guarantees of <see cref="EventOutboxExtensions.AddEventOutbox{TContext}" />: the delivery
///     background service is registered exactly once per context (no duplicate delivery loops), and the
///     interceptor, options and type resolver are all present.
/// </summary>
public sealed class EventOutboxExtensionsTests
{
    [Fact]
    public void AddEventOutbox_CalledTwice_RegistersSingleDeliveryService()
    {
        var services = new ServiceCollection();

        services.AddEventOutbox<OutboxTestDbContext>();
        services.AddEventOutbox<OutboxTestDbContext>();

        services.Count(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(EventOutboxDeliveryService<OutboxTestDbContext>))
            .Should().Be(1, "duplicate registration must not spawn a second delivery loop");
    }

    [Fact]
    public void AddEventOutbox_RegistersInterceptorOptionsAndResolver()
    {
        var services = new ServiceCollection();

        services.AddEventOutbox<OutboxTestDbContext>();

        services.Should().ContainSingle(d => d.ServiceType == typeof(EventOutboxInterceptor));
        services.Should().ContainSingle(d => d.ServiceType == typeof(EventOutboxOptions));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IEventOutboxTypeResolver));
    }

    [Fact]
    public void AddEventOutbox_AppliesConfigureAction()
    {
        var services = new ServiceCollection();

        services.AddEventOutbox<OutboxTestDbContext>(o => o.BatchSize = 42);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<EventOutboxOptions>().BatchSize.Should().Be(42);
    }
}
