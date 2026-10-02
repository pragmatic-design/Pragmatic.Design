using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     The «now» a query filter reads comes from the clock the executor was given.
/// </summary>
/// <remarks>
///     <para>
///         <c>FilterContext.Now</c> is public surface: a lifecycle step or a query filter reads it as
///         <c>context.Now</c>. The executor built it from <c>DateTimeOffset.UtcNow</c> while its four
///         siblings on the same save path — auditing, soft delete, the audit log, the unit of work —
///         all take an injected <see cref="TimeProvider" />.
///     </para>
///     <para>
///         ⚠️ The consequence is narrow and exact: a test that fixes the clock and expects a temporal
///         filter to see the fixed instant sees the wall clock instead. It is invisible in the common
///         case, because the two differ by milliseconds; it becomes visible precisely when somebody
///         tries to pin time, which is when somebody is trying to prove a temporal filter.
///     </para>
///     <para>
///         ⚠️ <c>PRAG0900</c> exists to refuse that line and does not run here: no runtime project
///         references <c>Pragmatic.Temporal.Analyzers</c>, and <c>Directory.Build.props</c> does not
///         apply it. The framework stated the rule and was not held to it.
///     </para>
/// </remarks>
public class TheExecutorAsksTheClockItWasGivenTests
{
    private static readonly DateTimeOffset Pinned = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A fixed clock reaches the context the filters are built from.</summary>
    [Fact]
    public void AFixedClock_IsTheNowAFilterReads()
    {
        var executor = Build(new FixedClock(Pinned));

        executor.GetOrBuildFilterContext().Now.Should().Be(Pinned,
            "a filter comparing against context.Now cannot be proven if the value is the wall clock");
    }

    /// <summary>
    ///     The control: without a clock the executor still works, from the system one.
    /// </summary>
    /// <remarks>
    ///     Without it, "the clock is used" is satisfied by an executor that requires one and throws
    ///     otherwise — which would break every caller that builds it with the parameterless
    ///     constructor, and there are several.
    /// </remarks>
    [Fact]
    public void WithNoClockGiven_TheSystemOneIsUsed()
    {
        var before = DateTimeOffset.UtcNow;
        var now = Build(timeProvider: null).GetOrBuildFilterContext().Now;

        now.Should().BeOnOrAfter(before);
        now.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     The second control: two executors on two clocks do not agree.
    /// </summary>
    /// <remarks>
    ///     Without it, the first assertion is satisfied by an executor that returns a constant — which
    ///     would match <c>Pinned</c> and be just as untestable as the wall clock, in the other
    ///     direction.
    /// </remarks>
    [Fact]
    public void TwoClocks_GiveTwoAnswers()
    {
        var later = Pinned.AddHours(3);

        Build(new FixedClock(Pinned)).GetOrBuildFilterContext().Now
            .Should().NotBe(Build(new FixedClock(later)).GetOrBuildFilterContext().Now);
    }

    private static EfCoreQueryExecutor Build(TimeProvider? timeProvider) =>
        new(
            filterProvider: null,
            filterMapComposer: null,
            filterToggle: null,
            cacheStack: null,
            logger: null,
            tenantContext: null,
            currentUser: null,
            cacheStackResolver: null,
            timeProvider: timeProvider);

    /// <summary>A clock that does not move, which is the whole point of injecting one.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
