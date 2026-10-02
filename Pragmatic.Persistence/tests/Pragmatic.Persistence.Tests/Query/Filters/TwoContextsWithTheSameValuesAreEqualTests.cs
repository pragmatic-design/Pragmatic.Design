using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.Tests.Query.Filters;

/// <summary>
///     Two <see cref="FilterContext" />s carrying the same values are the same context.
/// </summary>
/// <remarks>
///     <para>
///         It is a <c>record</c>, so it promises that. It could not keep the promise: the synthesized
///         equality compares <see cref="FilterContext.DisabledFilters" /> and
///         <see cref="FilterContext.DisabledQueryFilterNames" /> by reference, and both default to a
///         fresh set — so two contexts built the same way were never equal, and the failure printed
///         two identical objects, which is the worst shape a failure can take.
///     </para>
///     <para>
///         ⚠️ The hash code deliberately leaves the two sets out. See the file for why; these tests
///         assert that choice rather than describe it, because it is the part a reader will not guess.
///     </para>
/// </remarks>
public class TwoContextsWithTheSameValuesAreEqualTests
{
    private static readonly DateTimeOffset Instant = new(2026, 3, 14, 15, 9, 26, TimeSpan.Zero);

    /// <summary>The setpoint: same values in, one context out.</summary>
    [Fact]
    public void TwoContextsBuiltTheSameWay_AreEqual()
    {
        var left = FilterContext.At(Instant);
        var right = FilterContext.At(Instant);

        left.Should().Be(right, "a record is chosen for value semantics and these carry the same values");
    }

    /// <summary>And equal objects agree on their hash code, which the contract requires.</summary>
    [Fact]
    public void TwoContextsBuiltTheSameWay_ShareAHashCode()
    {
        FilterContext.At(Instant).GetHashCode().Should().Be(FilterContext.At(Instant).GetHashCode());
    }

    /// <summary>The sets are compared by content, not by which instance holds them.</summary>
    [Fact]
    public void TwoContextsDisablingTheSameFilters_AreEqual()
    {
        var left = FilterContext.At(Instant) with
        {
            DisabledFilters = new HashSet<Type> { typeof(string), typeof(int) },
            DisabledQueryFilterNames = new HashSet<string> { "SoftDelete" },
        };

        var right = FilterContext.At(Instant) with
        {
            DisabledFilters = new HashSet<Type> { typeof(int), typeof(string) },
            DisabledQueryFilterNames = new HashSet<string> { "SoftDelete" },
        };

        left.Should().Be(right, "the sets hold the same members, and a set has no order to disagree on");
    }

    /// <summary>⚠️ The first control: one more disabled filter is a different context.</summary>
    /// <remarks>
    ///     Equality that compares nothing would satisfy every assertion above. This is what says the
    ///     sets are read.
    /// </remarks>
    [Fact]
    public void AContextDisablingOneMoreFilter_IsNotEqual()
    {
        var left = FilterContext.At(Instant) with { DisabledFilters = new HashSet<Type> { typeof(string) } };
        var right = FilterContext.At(Instant) with
        {
            DisabledFilters = new HashSet<Type> { typeof(string), typeof(int) },
        };

        left.Should().NotBe(right);
    }

    /// <summary>⚠️ The second control: the named query filters are read too, and separately.</summary>
    [Fact]
    public void AContextLiftingOneMoreNamedQueryFilter_IsNotEqual()
    {
        var left = FilterContext.At(Instant) with
        {
            DisabledQueryFilterNames = new HashSet<string> { "SoftDelete" },
        };

        var right = FilterContext.At(Instant) with
        {
            DisabledQueryFilterNames = new HashSet<string> { "SoftDelete", "Tenant" },
        };

        left.Should().NotBe(right);
    }

    /// <summary>⚠️ The third control: the properties synthesized equality would handle still count.</summary>
    /// <remarks>
    ///     Hand-written equality replaces the synthesized one wholesale, so a forgotten property is a
    ///     silent widening — two contexts differing in it would compare equal. <c>Now</c> is the one
    ///     that matters most: it exists so a temporal read can be pinned.
    /// </remarks>
    [Fact]
    public void AContextAtAnotherInstant_IsNotEqual()
    {
        FilterContext.At(Instant).Should().NotBe(FilterContext.At(Instant.AddTicks(1)));
    }

    /// <summary>⚠️ And the rest of them, one at a time.</summary>
    [Fact]
    public void AContextDifferingInAnyOtherProperty_IsNotEqual()
    {
        var baseline = FilterContext.At(Instant);

        (baseline with { TenantId = "acme" }).Should().NotBe(baseline);
        (baseline with { UserId = "u1" }).Should().NotBe(baseline);
        (baseline with { Mode = FilterMode.Raw }).Should().NotBe(baseline);
    }

    /// <summary>
    ///     The hash code leaves the sets out, on purpose, and this is where that is stated.
    /// </summary>
    /// <remarks>
    ///     Two contexts differing only in a disabled filter collide. That is legal — the contract runs
    ///     one way, equal objects must agree — and it is the price of the property below.
    /// </remarks>
    [Fact]
    public void TheHashCode_LeavesTheSetsOut()
    {
        var left = FilterContext.At(Instant) with { DisabledFilters = new HashSet<Type> { typeof(string) } };
        var right = FilterContext.At(Instant);

        left.GetHashCode().Should().Be(right.GetHashCode(),
            "a set inside a hash code is a key that moves when the caller mutates the set it passed");
    }

    /// <summary>What that choice buys: a context stays findable after its set is mutated.</summary>
    /// <remarks>
    ///     ⚠️ The properties are <c>IReadOnlySet</c> by contract, and nothing stops a caller passing a
    ///     <see cref="HashSet{T}" /> it goes on mutating. Hashing the members would move the key and
    ///     lose the entry; this is the case that would fail.
    /// </remarks>
    [Fact]
    public void AContextStaysFindable_AfterTheSetItWasBuiltFromIsMutated()
    {
        var disabled = new HashSet<Type> { typeof(string) };
        var context = FilterContext.At(Instant) with { DisabledFilters = disabled };
        var map = new Dictionary<FilterContext, string> { [context] = "composed" };

        disabled.Add(typeof(int));

        map.TryGetValue(context, out var found).Should().BeTrue();
        found.Should().Be("composed");
    }
}
