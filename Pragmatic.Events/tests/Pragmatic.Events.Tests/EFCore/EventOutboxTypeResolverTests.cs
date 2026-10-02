using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events.EFCore.Outbox;

namespace Pragmatic.Events.Tests.EFCore;

/// <summary>
///     The outbox delivery loop must resolve event types through a closed allowlist instead of
///     <c>Type.GetType(dbString)</c> (a polymorphic-deserialization gadget surface). These tests
///     verify the fail-closed allowlist and that it is built from the registered handler types.
/// </summary>
public class EventOutboxTypeResolverTests
{
    private sealed record AllowedEvent(DateTimeOffset OccurredAt) : IDomainEvent;
    private sealed record ForbiddenEvent(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class AllowedHandler : IDomainEventHandler<AllowedEvent>
    {
        public Task HandleAsync(AllowedEvent domainEvent, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void Resolve_AllowlistedType_ResolvesByAllForms()
    {
        var resolver = new EventOutboxTypeResolver([typeof(AllowedEvent)]);

        resolver.Resolve(typeof(AllowedEvent).AssemblyQualifiedName!).Should().Be(typeof(AllowedEvent));
        resolver.Resolve(typeof(AllowedEvent).FullName!).Should().Be(typeof(AllowedEvent));
        resolver.Resolve(typeof(AllowedEvent).Name).Should().Be(typeof(AllowedEvent));
    }

    [Fact]
    public void Resolve_UnknownType_ReturnsNull_FailClosed()
    {
        var resolver = new EventOutboxTypeResolver([typeof(AllowedEvent)]);

        // A type that is real but not allowlisted, and a fabricated name, both reject.
        resolver.Resolve(typeof(ForbiddenEvent).AssemblyQualifiedName!).Should().BeNull();
        resolver.Resolve("System.Diagnostics.Process, System.Diagnostics.Process").Should().BeNull();
        resolver.Resolve("Totally.Made.Up.Type").Should().BeNull();
    }

    [Fact]
    public void AddEventOutbox_RegistersResolver_SeededFromRegisteredHandlers()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDomainEventHandler<AllowedEvent>, AllowedHandler>();
        services.AddEventOutbox<TestContext>();

        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IEventOutboxTypeResolver>();

        resolver.Resolve(typeof(AllowedEvent).FullName!).Should().Be(typeof(AllowedEvent));
        resolver.Resolve(typeof(ForbiddenEvent).FullName!).Should().BeNull(); // no handler ⇒ not allowlisted
    }

    private sealed class TestContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public TestContext(Microsoft.EntityFrameworkCore.DbContextOptions<TestContext> options) : base(options) { }
    }
}
