using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Events.EFCore;

/// <summary>
///     Wires the entity side of domain events into a <c>DbContext</c>.
/// </summary>
public static class DomainEventsDbContextOptionsBuilderExtensions
{
    /// <summary>
    ///     Raises the events an entity declares with <c>[Raises&lt;TEvent&gt;(on: ...)]</c> at the
    ///     lifecycle transition that declares them.
    /// </summary>
    /// <param name="optionsBuilder">The DbContext options builder.</param>
    /// <returns>The options builder for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         <b>Raising only.</b> Dispatch belongs to <c>EfCoreUnitOfWork</c>, which takes the events
    ///         after a successful save and either hands them to whoever owns the commit or dispatches
    ///         them itself — in the scope that asked for the write.
    ///     </para>
    ///     <para>
    ///         An interceptor does not dispatch here: to avoid a captive dependency on the scoped
    ///         dispatcher it would need a scope of its own, and that scope is the problem. A fresh one
    ///         resolves a fresh tenant context, no tenant is resolved in it, and every fail-closed query
    ///         filter then hides the rows the handler was called to act on. The handler runs, finds
    ///         nothing, writes nothing and reports success.
    ///     </para>
    ///     <para>
    ///         Register a dispatcher with <c>AddInMemoryDomainEvents()</c>; the generated event-handler
    ///         registration already does.
    ///     </para>
    /// </remarks>
    public static DbContextOptionsBuilder UseDomainEvents(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        optionsBuilder.AddInterceptors(new LifecycleEventsInterceptor());

        return optionsBuilder;
    }
}
