// Pragmatic.Composition.HostWiring.Tests - The publisher's need is not the handler's
// Asserts on the generated host: whether the dispatcher is registered is decided there.

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     An operation that publishes domain events needs the dispatcher whether or not anybody
///     handles them.
/// </summary>
/// <remarks>
///     Registering the dispatcher only when some assembly declares an <c>[EventHandler]</c> is not
///     enough. <c>LoginUser</c> takes <c>IDomainEventDispatcher</c> as a dependency and publishes on
///     every sign-in, so an application with local accounts and no handler would fail every sign-in
///     with a DI error. The dispatcher is inert with no handler behind it.
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class APublisherGetsADispatcherWithoutHandlersTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    private const string Dispatcher = "services.AddInMemoryDomainEvents();";

    [Fact]
    public void AHostWithEventsAndNoHandler_RegistersTheDispatcher()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Bare, HostServices);

        lines.Should().Contain(Dispatcher,
            "Pragmatic.Events is on the compilation, and an operation that publishes resolves the dispatcher "
            + $"with or without a handler; Host.Services.g.cs has {lines.Count} lines");
    }

    [Fact]
    public void TheDispatcher_IsRegisteredOnce()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Count(line => line == Dispatcher).Should().Be(1);
    }
}
