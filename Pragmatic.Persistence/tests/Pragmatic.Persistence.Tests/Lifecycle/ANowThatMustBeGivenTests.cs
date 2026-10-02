using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Lifecycle;

/// <summary>
///     The instant a context carries is given to it, never taken from the wall clock.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A default of <c>= DateTimeOffset.UtcNow</c> on <c>Now</c> would make any construction
///         that does not write it read the clock: it could not fail, it could not be pinned, and it
///         would look right at the call site because the property is named for the value it should
///         carry.
///     </para>
///     <para>
///         That guarantee is the compiler's — <c>required</c> — so the assertions here are about the
///         other half: that the factories which absorb the noise do not quietly reintroduce the
///         default under a new name.
///     </para>
/// </remarks>
public class ANowThatMustBeGivenTests
{
    private static readonly DateTimeOffset Pinned = new(2026, 3, 14, 15, 9, 26, TimeSpan.Zero);

    [Fact]
    public void ALifecycleContextAtAnInstant_CarriesThatInstant()
    {
        LifecycleContext.At(Pinned).Now.Should().Be(Pinned);
    }

    [Fact]
    public void AFilterContextAtAnInstant_CarriesThatInstant()
    {
        FilterContext.At(Pinned).Now.Should().Be(Pinned);
    }

    /// <summary>
    ///     The control: two calls with the same instant are equal.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         If anything inside the factory read the clock, two contexts built from one instant would
    ///         differ — the wall-clock default again, wearing a factory's name.
    ///     </para>
    ///     <para>
    ///         Whole-record equality on both. <c>FilterContext</c>'s two set properties default to a
    ///         fresh <c>HashSet</c> each, which synthesized record equality would compare by reference,
    ///         so two independently built contexts would <b>never</b> be equal. The type compares them
    ///         by content — <c>TwoContextsWithTheSameValuesAreEqualTests</c> is where that lives.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TwoContextsBuiltFromOneInstant_AreTheSameContext()
    {
        LifecycleContext.At(Pinned).Should().Be(LifecycleContext.At(Pinned));

        FilterContext.At(Pinned).Should().Be(FilterContext.At(Pinned));
    }

    /// <summary>
    ///     A pinned <c>TimeProvider</c> is read once, and its instant is what the context carries.
    /// </summary>
    /// <remarks>
    ///     The overload exists so a caller holding a provider does not have to unwrap it at every call
    ///     site; it must read that provider and nothing else.
    /// </remarks>
    [Fact]
    public void AContextFromAPinnedProvider_CarriesTheProvidersInstant()
    {
        var provider = new PinnedTimeProvider(Pinned);

        LifecycleContext.At(provider).Now.Should().Be(Pinned);
        FilterContext.At(provider).Now.Should().Be(Pinned);
    }

    /// <summary>
    ///     Everything else a context carries survives being rebuilt from one of these.
    /// </summary>
    /// <remarks>
    ///     The factories are the entry point for a caller that would otherwise write an object
    ///     initialiser, so <c>with</c> on top of them has to work — otherwise every such call site
    ///     is a rewrite rather than an edit.
    /// </remarks>
    [Fact]
    public void AContextRebuiltWithMoreValues_KeepsTheInstant()
    {
        var context = FilterContext.At(Pinned) with { Mode = FilterMode.Admin, UserId = "u-1" };

        context.Now.Should().Be(Pinned);
        context.Mode.Should().Be(FilterMode.Admin);
        context.UserId.Should().Be("u-1");
    }

    private sealed class PinnedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
