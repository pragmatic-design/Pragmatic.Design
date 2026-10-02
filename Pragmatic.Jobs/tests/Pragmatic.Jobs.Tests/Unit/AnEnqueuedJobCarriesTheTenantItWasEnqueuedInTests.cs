using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.MultiTenancy;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     A job enqueued inside a tenant carries it, so the run can read the rows the caller could.
/// </summary>
/// <remarks>
///     <para>
///         <c>JobInstance.TenantId</c> and <c>JobContext.TenantId</c> both exist, and the only two
///         things that ever wrote them were the recurring scheduler — from the definition — and a
///         completed job propagating to its continuation. The ad-hoc path, which is the one an
///         application uses, left it null.
///     </para>
///     <para>
///         ⚠️ <b>The failure is silent, and this repository has already written down what it looks
///         like.</b> With no tenant resolved, a query over an <c>ITenantEntity</c> reads zero rows —
///         not an error, not an empty database — and the job reports success on having done nothing.
///     </para>
///     <para>
///         ⚠️ <b>No explicit tenant parameter, deliberately.</b> The issue suggested one on
///         <c>ScheduleAsync</c> "either way". <c>TenantScope.BeginScope</c> is already the framework's
///         way to say "do this as that tenant" — it is what the event outbox uses to restore one
///         before dispatching — and capturing the ambient tenant here makes enqueuing obey it. A
///         parameter would be a second mechanism for something already solved, and the second case
///         below is the one that shows the first still works.
///     </para>
/// </remarks>
public class AnEnqueuedJobCarriesTheTenantItWasEnqueuedInTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static (JobScheduler Scheduler, InMemoryJobStore Store) CreateSut(ITenantContext? tenant)
    {
        var store = new InMemoryJobStore(new TestClock(Now));
        var scheduler = new JobScheduler(
            store,
            new TestClock(Now),
            new JobsOptions(),
            NullLogger<JobScheduler>.Instance,
            Pragmatic.Serialization.PragmaticJsonOptions.Default,
            new KnowsEverything(),
            tenant);

        return (scheduler, store);
    }

    /// <summary>The setpoint: enqueued with a tenant resolved, the row says which.</summary>
    [Fact]
    public async Task AJobEnqueuedWithATenantResolved_IsPersistedWithIt()
    {
        var (scheduler, store) = CreateSut(new FixedTenant("acme"));

        var id = await scheduler.ScheduleAsync<SampleJob>();

        (await store.GetAsync(id))!.TenantId.Should().Be("acme",
            "the run happens outside the request, so the tenant has to be on the row");
    }

    /// <summary>
    ///     ⚠️ The first control: no tenant resolved leaves it null.
    /// </summary>
    /// <remarks>
    ///     A single-tenant application resolves none, and the column has to stay empty rather than
    ///     acquire a value nothing put there. Null is also what the processor reads as "no scope to
    ///     open".
    /// </remarks>
    [Fact]
    public async Task AJobEnqueuedWithNoTenant_CarriesNone()
    {
        var (scheduler, store) = CreateSut(new FixedTenant(null));

        var id = await scheduler.ScheduleAsync<SampleJob>();

        (await store.GetAsync(id))!.TenantId.Should().BeNull();
    }

    /// <summary>
    ///     ⚠️ The second control: an application with no multi-tenancy at all still enqueues.
    /// </summary>
    /// <remarks>
    ///     <c>ITenantContext</c> is optional here because <c>Pragmatic.MultiTenancy</c> is optional:
    ///     an application that never registered one must schedule exactly as it did before. Microsoft's
    ///     container fills a constructor parameter it cannot resolve from its default value, which is
    ///     what makes null reachable — and this is what measures it rather than trusting it.
    /// </remarks>
    [Fact]
    public async Task AnApplicationWithNoTenantContextAtAll_StillEnqueues()
    {
        var (scheduler, store) = CreateSut(tenant: null);

        var id = await scheduler.ScheduleAsync<SampleJob>();

        var job = await store.GetAsync(id);
        job.Should().NotBeNull();
        job!.TenantId.Should().BeNull();
    }

    /// <summary>
    ///     ⚠️ And the parameter that was not added: a caller who knows says it with a scope.
    /// </summary>
    /// <remarks>
    ///     This is the case an explicit <c>tenantId</c> argument on <c>ScheduleAsync</c> would have
    ///     served. <c>TenantScope</c> is itself an <c>ITenantContext</c> reading the ambient scope, and
    ///     the registered <c>AmbientTenantContext</c> falls back to the same one outside a request —
    ///     so a background caller that opens a scope is answered by it, which is why the parameter
    ///     would have been a second way to say the same thing.
    /// </remarks>
    [Fact]
    public async Task ACallerOutsideARequest_SaysWhichTenantWithAScope()
    {
        var (scheduler, store) = CreateSut(new TenantScope());

        using (TenantScope.BeginScope("beta"))
        {
            var id = await scheduler.ScheduleAsync<SampleJob>();

            (await store.GetAsync(id))!.TenantId.Should().Be("beta");
        }
    }

    /// <summary>A job that does nothing: what is under test is the row, not the run.</summary>
    private sealed class SampleJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedTenant(string? tenantId) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;

        public string? TenantName => TenantId;

        public bool IsResolved => TenantId is { Length: > 0 };
    }
}
