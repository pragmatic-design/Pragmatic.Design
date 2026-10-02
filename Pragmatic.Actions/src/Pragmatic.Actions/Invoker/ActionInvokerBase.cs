using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Actions.Diagnostics;
using Pragmatic.Actions.Pipeline;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Actions.Invoker;

/// <summary>
///     Base class for action invokers with shared pipeline logic.
///     Not intended for direct use — use <see cref="DomainActionInvoker{TAction, TReturn}" />
///     or <see cref="VoidDomainActionInvoker{TAction}" /> instead.
/// </summary>
/// <remarks>
///     This base class handles:
///     <list type="bullet">
///         <item>Logger and filter resolution</item>
///         <item>Activity/telemetry setup via <see cref="ActionsDiagnostics" /></item>
///         <item>Common logging operations</item>
///     </list>
///     Derived classes implement the specific pipeline execution.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract partial class ActionInvokerBase
{
    protected readonly List<IActionFilter> GlobalFilters;

    // Named _logger so [LoggerMessage] source generator can locate it automatically
    protected readonly ILogger _logger;

    /// <summary>The DI container, kept for resolving the domain-event dispatcher when auto-raising events.</summary>
    protected readonly IServiceProvider ServiceProvider;

    /// <summary>Resolves the request's compensation scope and holds the undos this invoker registers.</summary>
    protected readonly Pragmatic.Actions.Compensation.CompensationCoordinator Compensation;

    protected ActionInvokerBase(IServiceProvider serviceProvider, Type invokerType)
    {
        ThrowIfNull(serviceProvider);

        ServiceProvider = serviceProvider;

        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        _logger = loggerFactory?.CreateLogger(invokerType) ?? NullLogger.Instance;

        GlobalFilters = (serviceProvider.GetService<IEnumerable<IActionFilter>>() ?? []).ToList();

        Compensation = new Pragmatic.Actions.Compensation.CompensationCoordinator(serviceProvider);
    }

    /// <summary>
    ///     Runs the action's <c>[InvalidatesCache]</c> declaration, if it carries one, after the commit.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is post-commit work and it is called from inside the post-commit try/catch for the
    ///     same reason the event dispatch is: the write is already committed, so a cache that refuses
    ///     the invalidation must not turn a completed operation into a thrown failure the caller
    ///     retries. A deployment with no <c>ICacheStack</c> at all is logged, not thrown, for the same
    ///     reason.
    /// </remarks>
    /// <param name="action">The action that has just committed.</param>
    /// <param name="kind">"Action" or "VoidAction", for the log.</param>
    /// <param name="actionName">The action's type name, for the log.</param>
    /// <param name="ct">Cancellation token.</param>
    protected Task InvalidateCacheAsync(object action, string kind, string actionName, CancellationToken ct)
        => Pragmatic.Actions.Cache.CacheInvalidation.RunAsync(
            action,
            ServiceProvider,
            () => LogCacheInvalidationSkipped(kind, actionName),
            ct);

    /// <summary>
    ///     Dispatches the domain events an operation declared via <c>[Raises&lt;T&gt;]</c> after a successful
    ///     commit. The generated invoker builds the event instances (ctor filled by name from the operation's
    ///     inputs); this central path resolves the dispatcher and flushes them — so the entity needs no behavior.
    /// </summary>
    protected async Task DispatchRaisedEventsAsync(
        IReadOnlyList<Pragmatic.Events.IDomainEvent> events,
        CancellationToken ct)
    {
        if (events.Count == 0)
            return;

        var dispatcher = ServiceProvider.GetService<Pragmatic.Events.IDomainEventDispatcher>();
        if (dispatcher is null)
        {
            // Declaring [Raises<T>] with no dispatcher registered drops the events — a common
            // "forgot to wire Pragmatic.Events" foot-gun — so it is logged rather than silent.
            LogRaisedEventsDropped(events.Count);
            return;
        }

        await dispatcher.DispatchAsync(events, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates sorted list of global filters.
    /// </summary>
    protected List<IActionFilter> GetSortedGlobalFilters()
    {
        var result = new List<IActionFilter>(GlobalFilters);
        result.Sort((a, b) => a.Order.CompareTo(b.Order));
        return result;
    }

    /// <summary>
    ///     Configures activity with common tags following OTel conventions.
    /// </summary>
    protected static void ConfigureActivity(Activity? activity, string actionTypeName, string kind, int filterCount = 0)
    {
        if (activity is null) return;
        activity.SetTag(ActionTags.Name, actionTypeName);
        activity.SetTag(ActionTags.Kind, kind);
        if (filterCount > 0)
            activity.SetTag(ActionTags.FilterCount, filterCount);
    }

    /// <summary>
    ///     Sets activity result tags for success.
    /// </summary>
    protected static void SetActivitySuccess(Activity? activity)
    {
        activity?.SetTag(ActionTags.Result, "success");
        activity?.SetSuccess();
    }

    /// <summary>
    ///     Sets activity result tags for business failure.
    /// </summary>
    protected static void SetActivityFailure(Activity? activity, string errorCode)
    {
        activity?.SetTag(ActionTags.Result, "failure");
        activity?.SetTag(ActionTags.ErrorCode, errorCode);
        activity?.SetFailure(errorCode);
    }

    /// <summary>
    ///     Sets activity result tags for filter short-circuit.
    /// </summary>
    protected static void SetActivityShortCircuit(Activity? activity, string errorCode)
    {
        activity?.SetTag(ActionTags.Result, "short-circuited");
        activity?.SetTag(ActionTags.ErrorCode, errorCode);
        activity?.SetFailure(errorCode, "Filter short-circuited");
    }

    /// <summary>
    ///     Records an exception on the activity following OTel semantic conventions.
    /// </summary>
    protected static void SetActivityException(Activity? activity, Exception ex)
    {
        activity?.RecordException(ex);
    }
}
