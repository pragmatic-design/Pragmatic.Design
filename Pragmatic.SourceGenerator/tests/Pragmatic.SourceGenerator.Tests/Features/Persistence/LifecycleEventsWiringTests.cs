using System;
using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[Raises&lt;TEvent&gt;]</c> makes the generator emit
///     <c>IRaisesLifecycleEvents.RaiseLifecycleEvents(...)</c> on the entity, and
///     <c>LifecycleEventsInterceptor</c> is the only thing that calls it. The generated DbContext
///     wiring registers it: with a dispatching interceptor alone, on the standard path — the one the
///     framework builds by itself — the attribute would produce code nobody invokes, and only the
///     manual <c>UseDomainEvents()</c> extension would wire it.
/// </summary>
/// <remarks>
///     Order is part of the contract, not decoration. The lifecycle interceptor raises the events in
///     <c>SavingChanges</c>; the outbox capture interceptors read the entity's events in
///     <c>SavingChanges</c> too, so a lifecycle interceptor registered after them would raise events
///     that never reach the outbox. EF Core invokes interceptors in registration order.
/// </remarks>
public class LifecycleEventsWiringTests
{
    [Fact]
    public void Registration_WithEvents_WiresLifecycleEventsInterceptor()
    {
        Render(hasEvents: true).Should().Contain(
            "new global::Pragmatic.Events.EFCore.LifecycleEventsInterceptor()",
            "[Raises<T>] generates RaiseLifecycleEvents on the entity and only this interceptor calls it");
    }

    /// <summary>
    ///     Nothing dispatches from inside the save.
    /// </summary>
    /// <remarks>
    ///     An interceptor dispatching in <c>SavedChangesAsync</c> would need a scope of its own to avoid
    ///     a captive dependency on the scoped dispatcher, and the scope is the defect: a fresh one
    ///     resolves a fresh tenant context, none is resolved in it, and every fail-closed query filter
    ///     then hides the rows the handler is called to act on — so it runs, finds nothing, writes
    ///     nothing, and reports success. Dispatch belongs to <c>EfCoreUnitOfWork</c>.
    ///     <para>
    ///         Counted rather than looked up by name: switching events on adds exactly one interceptor,
    ///         the lifecycle one, so a second — whatever it is called — fails here.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Registration_WithEvents_AddsOnlyTheLifecycleInterceptor()
    {
        var added = Interceptors(Render(hasEvents: true)) - Interceptors(Render(hasEvents: false));

        added.Should().Be(1, "events add the interceptor that raises them, and nothing that dispatches");

        // The control: the count sees an interceptor when one is there, or the 1 above proves nothing.
        Interceptors(Render(hasEvents: true, hasEventOutbox: true)).Should().BeGreaterThan(
            Interceptors(Render(hasEvents: true)), "the outbox's capture interceptor is counted too");
    }

    /// <summary>
    ///     The unit of work is handed a dispatcher, so the path it owns is not a branch nobody reaches.
    /// </summary>
    /// <remarks>
    ///     <c>EfCoreUnitOfWork</c> dispatches the events of a save that no invoker is orchestrating, and
    ///     it can only do that with an <c>IDomainEventDispatcher</c>. A registration that constructs it
    ///     with the DbContext alone leaves the parameter always null: every such write leaves its events
    ///     on the entity, undelivered and unreported. <c>GetService</c>, not
    ///     <c>GetRequiredService</c> — an application with no domain events registers no dispatcher, and
    ///     that must not stop a unit of work from being built.
    /// </remarks>
    [Fact]
    public void Registration_HandsTheUnitOfWorkADispatcher()
    {
        Render(hasEvents: true).Should().Contain(
            "sp.GetService<global::Pragmatic.Events.IDomainEventDispatcher>()",
            "without one the unit of work's own dispatch is a branch that can never run");
    }

    [Fact]
    public void Registration_WithoutEvents_DoesNotWireLifecycleEventsInterceptor()
    {
        Render(hasEvents: false).Should().NotContain("LifecycleEventsInterceptor");
    }

    [Fact]
    public void Registration_RaisesLifecycleEventsBeforeTheOutboxCapturesThem()
    {
        var source = Render(hasEvents: true, hasEventOutbox: true);

        IndexOf(source, "LifecycleEventsInterceptor").Should().BeLessThan(
            IndexOf(source, "EventOutboxInterceptor"),
            "the outbox captures the entity's events in SavingChanges — anything raised after it is lost");
    }

    /// <summary>
    ///     How many interceptors the wiring adds, in either form it writes them: constructed
    ///     (<c>new …Interceptor(</c>) or resolved (<c>GetRequiredService&lt;…Interceptor&gt;()</c>).
    /// </summary>
    private static int Interceptors(string source)
        => System.Text.RegularExpressions.Regex.Matches(source, @"Interceptor(?:\(|>\()").Count;

    private static int IndexOf(string source, string needle)
    {
        var index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"'{needle}' must be present in the generated wiring");
        return index;
    }

    private static string Render(bool hasEvents, bool hasEventOutbox = false)
    {
        var template = new DbContextRegistrationTemplate(
            ImmutableArray.Create(new BoundaryDbContextModel
            {
                Namespace = "MyApp.Sales.Entities",
                ClassName = "SalesDbContext",
                BoundaryName = "Sales",
                BoundaryTypeName = "MyApp.Sales.SalesBoundary",
                EfCoreProvider = EfCoreProvider.PostgreSql,
                HasEventOutbox = hasEventOutbox,
            }),
            "MyApp",
            hasMultiTenancy: false,
            hasEvents: hasEvents);

        return template.RenderOutput().Text;
    }
}
