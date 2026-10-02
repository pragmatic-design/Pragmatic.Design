using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Extensions;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     A job type any registered assembly declares can be scheduled and run, and one no
///     assembly declares is refused where the caller is.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The generator emits a registry per assembly. A registration that <c>Replace</c>s
///         whatever is there leaves the last one loaded as the only one that answers: two modules
///         declaring jobs cancel each other out, and a job type shipped by a <em>package</em> — the
///         messaging bridge's <c>PublishMessageJob</c> — never runs: <c>EnableScheduledMessages()</c>
///         registers a scheduler whose work the runner then refuses as "Unknown job type".
///     </para>
///     <para>
///         ⚠️ And that refusal comes too late to help. For a redelivered message the original has
///         already been acknowledged, so what is left is not a dead letter but nothing.
///     </para>
/// </remarks>
public class AJobTypeFromAnotherAssemblyRunsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private const string Theirs = "Another.Assembly.TheirJob";

    [Fact]
    public void ACompositeAsksEveryRegistry_NotOnlyTheLast()
    {
        var registry = new CompositeJobTypeRegistry([new OneAssembly("Mine.MyJob"), new OneAssembly(Theirs)]);

        registry.Knows("Mine.MyJob").Should().BeTrue();
        registry.Knows(Theirs).Should().BeTrue("the second assembly's jobs are not shadowed by the first");
    }

    /// <summary>
    ///     And what it answers about a job comes from the registry that knows it, not from the first
    ///     one asked.
    /// </summary>
    /// <remarks>
    ///     Without this, a composite that found the right registry for <c>Knows</c> and then asked the
    ///     first one for everything else would look correct here and run every job with the wrong
    ///     priority and the wrong retry budget.
    /// </remarks>
    [Fact]
    public void WhatItAnswers_ComesFromTheRegistryThatKnowsTheJob()
    {
        var registry = new CompositeJobTypeRegistry(
            [new OneAssembly("Mine.MyJob", priority: 1), new OneAssembly(Theirs, priority: 7)]);

        registry.GetPriority(Theirs).Should().Be(7);
        registry.GetPriority("Mine.MyJob").Should().Be(1);
    }

    /// <summary>
    ///     The control: a type nobody declared is not known, and the composite says how many it asked.
    /// </summary>
    /// <remarks>
    ///     Without it, "every registry is asked" is satisfied by a composite that answers yes to
    ///     everything — which would put back exactly the failure this closes, one layer along.
    /// </remarks>
    [Fact]
    public void ATypeNobodyDeclared_IsNotKnown_AndTheFailureSaysWhatWasAsked()
    {
        var registry = new CompositeJobTypeRegistry([new OneAssembly("Mine.MyJob")]);

        registry.Knows("Nobody.KnowsThis").Should().BeFalse();

        var run = () => registry.ExecuteAsync(
            "Nobody.KnowsThis", null, new JobContext(Guid.NewGuid(), "Nobody.KnowsThis", Now, 1, 3),
            null!, CancellationToken.None);

        run.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Nobody.KnowsThis").And.Contain("Asked 1 registry");
    }

    /// <summary>
    ///     With no registry at all — <c>AddPragmaticJobs()</c> and no generated <c>AddDiscoveredJobs()</c> —
    ///     the registry the runtime resolves refuses a job and says that nothing was registered.
    /// </summary>
    /// <remarks>
    ///     The case a placeholder registry once stood for. The registration is always the
    ///     composite, so the background services' check for that placeholder could never be true, and
    ///     both went with it; this pins that the case still answers, through the real
    ///     registration.
    /// </remarks>
    [Fact]
    public async Task WithNoRegistryAtAll_AJobIsRefused_AndTheFailureSaysNoneWasRegistered()
    {
        using var provider = new ServiceCollection()
            .AddPragmaticJobs()
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<IJobTypeRegistry>();

        var run = () => registry.ExecuteAsync(
            "Nobody.KnowsThis", null, new JobContext(Guid.NewGuid(), "Nobody.KnowsThis", Now, 1, 3),
            provider, CancellationToken.None);

        (await run.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Unknown job type: Nobody.KnowsThis").And.Contain("No registry is registered");
    }

    /// <summary>
    ///     Scheduling a job no registry knows is refused where the caller is, not in the background.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A row scheduled for an unknown type can never run, and the caller has already been told
    ///     the work was accepted. For a scheduled message that is worse than an error: the message it
    ///     was carrying was acknowledged when the schedule was taken, so nothing is left to retry.
    /// </remarks>
    [Fact]
    public async Task SchedulingAJobNobodyKnows_IsRefusedAtTheCall()
    {
        var scheduler = Scheduler(new CompositeJobTypeRegistry([]));

        var run = () => scheduler.ScheduleAsync<SampleJob>();

        (await run.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("can never run");
    }

    /// <summary>The control: one that a registry does know is scheduled as before.</summary>
    [Fact]
    public async Task SchedulingAJobSomeRegistryKnows_Works()
    {
        var scheduler = Scheduler(new CompositeJobTypeRegistry([new OneAssembly(typeof(SampleJob).FullName!)]));

        var id = await scheduler.ScheduleAsync<SampleJob>();

        id.Should().NotBe(Guid.Empty);
    }

    private static JobScheduler Scheduler(IJobTypeRegistry registry)
        => new(
            new InMemoryJobStore(new TestClock(Now)),
            new TestClock(Now),
            new JobsOptions(),
            NullLogger<JobScheduler>.Instance,
            Pragmatic.Serialization.PragmaticJsonOptions.Default,
            registry);

    /// <summary>A job to schedule; what it does is not this class's subject.</summary>
    private sealed class SampleJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>One assembly's registry, as the generator writes one: it knows its own job types.</summary>
    private sealed class OneAssembly(string jobTypeFqn, int priority = 0) : IJobTypeRegistrySource
    {
        public bool Knows(string fqn) => fqn == jobTypeFqn;

        public object? DeserializeParameters(string fqn, string? json) => null;

        public string? SerializeParameters(string fqn, object? parameters) => null;

        public Task ExecuteAsync(
            string fqn, string? parametersJson, JobContext context,
            IServiceProvider serviceProvider, CancellationToken ct) => Task.CompletedTask;

        public JobRetryPolicy? GetRetryPolicy(string fqn) => null;

        public string? GetContinuationJobType(string fqn) => null;

        public int GetPriority(string fqn) => priority;

        public int GetMaxConcurrency(string fqn) => 0;
    }
}
