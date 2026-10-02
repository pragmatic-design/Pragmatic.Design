using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.Extensions;
using Pragmatic.Events.Tests.Fixtures;
using Xunit;

namespace Pragmatic.Events.Tests.Extensions;

/// <summary>
///     Tests for <see cref="DomainEventsServiceCollectionExtensions" />.
/// </summary>
public class DomainEventsServiceCollectionExtensionsTests
{
    // =========================================================================
    // AddInMemoryDomainEvents
    // =========================================================================

    [Fact]
    public void AddInMemoryDomainEvents_RegistersDispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInMemoryDomainEvents();

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetService<IDomainEventDispatcher>();
        dispatcher.Should().NotBeNull();
        dispatcher.Should().BeOfType<InMemoryEventDispatcher>();
    }

    [Fact]
    public void AddInMemoryDomainEvents_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInMemoryDomainEvents();
        services.AddInMemoryDomainEvents(); // Second call

        var descriptors = services.Where(d => d.ServiceType == typeof(IDomainEventDispatcher)).ToList();
        descriptors.Should().HaveCount(1, "TryAddScoped should prevent duplicates");
    }

    [Fact]
    public void AddInMemoryDomainEvents_RegistersAsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddInMemoryDomainEvents();

        var descriptor = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddInMemoryDomainEvents_ReturnsSameCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddInMemoryDomainEvents();

        result.Should().BeSameAs(services);
    }

    // =========================================================================
    // AddDomainEventHandler<THandler, TEvent>
    // =========================================================================

    [Fact]
    public void AddDomainEventHandler_RegistersHandler()
    {
        var services = new ServiceCollection();

        services.AddDomainEventHandler<TestEventHandler, TestDomainEvent>();

        var sp = services.BuildServiceProvider();
        var handlers = sp.GetServices<IDomainEventHandler<TestDomainEvent>>().ToList();
        handlers.Should().ContainSingle()
            .Which.Should().BeOfType<TestEventHandler>();
    }

    [Fact]
    public void AddDomainEventHandler_MultipleRegistrations_RegistersAll()
    {
        var services = new ServiceCollection();

        services.AddDomainEventHandler<TestEventHandler, TestDomainEvent>();
        services.AddDomainEventHandler<TestEventHandler, TestDomainEvent>();

        var sp = services.BuildServiceProvider();
        var handlers = sp.GetServices<IDomainEventHandler<TestDomainEvent>>().ToList();
        handlers.Should().HaveCount(2);
    }

    [Fact]
    public void AddDomainEventHandler_RegistersAsScoped()
    {
        var services = new ServiceCollection();

        services.AddDomainEventHandler<TestEventHandler, TestDomainEvent>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IDomainEventHandler<TestDomainEvent>));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddDomainEventHandler_ReturnsSameCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddDomainEventHandler<TestEventHandler, TestDomainEvent>();

        result.Should().BeSameAs(services);
    }

    // =========================================================================
    // =========================================================================

}
