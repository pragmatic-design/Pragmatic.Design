using Pragmatic.Testing.Assertions;

using static Pragmatic.Testing.Tests.Assertions.AssertionProbe;

namespace Pragmatic.Testing.Tests.Assertions;

/// <summary>
///     Ordering, approximation, and the boundaries — where an off-by-one in the comparison is
///     invisible unless the equal case is asserted explicitly.
/// </summary>
public class ComparableAssertionTests
{
    [Fact]
    public void BeGreaterOrEqualTo_AcceptsEqual_WhereBeGreaterThanDoesNot()
    {
        5.Should().BeGreaterOrEqualTo(5);
        5.Should().BeGreaterThanOrEqualTo(5);
        Fails(() => 5.Should().BeGreaterThan(5));
        Fails(() => 4.Should().BeGreaterOrEqualTo(5));
    }

    [Fact]
    public void BeLessOrEqualTo_AcceptsEqual_WhereBeLessThanDoesNot()
    {
        5.Should().BeLessOrEqualTo(5);
        5.Should().BeLessThanOrEqualTo(5);
        5.Should().BeLessThan(6);
        Fails(() => 5.Should().BeLessThan(5));
        Fails(() => 6.Should().BeLessOrEqualTo(5));
    }

    [Fact]
    public void BeInRange_IsInclusiveAtBothEnds()
    {
        5.Should().BeInRange(1, 10);
        1.Should().BeInRange(1, 10);
        10.Should().BeInRange(1, 10);

        Fails(() => 0.Should().BeInRange(1, 10));
        Fails(() => 11.Should().BeInRange(1, 10));
    }

    [Fact]
    public void BePositive_AndBeNegative_ExcludeZero()
    {
        1.Should().BePositive();
        (-1).Should().BeNegative();

        Fails(() => 0.Should().BePositive());
        Fails(() => 0.Should().BeNegative());
        Fails(() => (-1).Should().BePositive());
    }

    [Fact]
    public void Dates_OrderWithTheirOwnVocabulary()
    {
        var earlier = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        later.Should().BeAfter(earlier);
        later.Should().BeOnOrAfter(earlier);
        later.Should().BeOnOrAfter(later);
        earlier.Should().BeBefore(later);
        earlier.Should().BeOnOrBefore(later);
        earlier.Should().BeOnOrBefore(earlier);

        Fails(() => earlier.Should().BeAfter(later));
        Fails(() => later.Should().BeOnOrBefore(earlier));
        Fails(() => later.Should().BeBefore(later));
    }

    [Fact]
    public void BeCloseTo_AcceptsWithinThePrecision_Inclusive()
    {
        var instant = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        instant.AddMilliseconds(500).Should().BeCloseTo(instant, TimeSpan.FromSeconds(1));
        instant.AddSeconds(1).Should().BeCloseTo(instant, TimeSpan.FromSeconds(1));
        // Symmetric: being early is as close as being late.
        instant.AddSeconds(-1).Should().BeCloseTo(instant, TimeSpan.FromSeconds(1));

        Fails(() => instant.AddSeconds(2).Should().BeCloseTo(instant, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void BeCloseTo_OnOffsets_AndOnNullables()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        instant.AddMilliseconds(200).Should().BeCloseTo(instant, TimeSpan.FromSeconds(1));

        DateTimeOffset? nullable = instant.AddMilliseconds(200);
        nullable.Should().BeCloseTo(instant, TimeSpan.FromSeconds(1));

        // A null instant is close to nothing.
        DateTimeOffset? missing = null;
        Fails(() => missing.Should().BeCloseTo(instant, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void BeCloseTo_OnTimeSpans()
    {
        TimeSpan.FromSeconds(10).Should().BeCloseTo(TimeSpan.FromSeconds(10.5), TimeSpan.FromSeconds(1));
        Fails(() => TimeSpan.FromSeconds(10).Should().BeCloseTo(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void BeApproximately_OnEachFloatingType()
    {
        0.1d.Should().BeApproximately(0.11d, 0.02d);
        0.1f.Should().BeApproximately(0.11f, 0.02f);
        0.1m.Should().BeApproximately(0.11m, 0.02m);

        Fails(() => 0.1d.Should().BeApproximately(0.2d, 0.02d));
        Fails(() => 0.1f.Should().BeApproximately(0.2f, 0.02f));
        Fails(() => 0.1m.Should().BeApproximately(0.2m, 0.02m));
    }

    /// <summary>
    ///     Nullable value types order through the default comparer, which puts null before
    ///     everything. Without their own overloads they would fall back to the object assertions,
    ///     where none of this vocabulary exists.
    /// </summary>
    [Fact]
    public void Nullables_Order_WithNullBeforeEverything()
    {
        int? present = 5;
        int? missing = null;

        present.Should().BeGreaterThan(missing);
        present.Should().Be(5);
        missing.Should().BeNull();

        Fails(() => missing.Should().BeGreaterThan(present));
    }

    [Fact]
    public void UnsignedAndNarrowIntegers_HaveTheirOwnOverloads()
    {
        ((uint)5).Should().BeGreaterThan(4u);
        ((ulong)5).Should().BeGreaterThan(4ul);
        ((short)5).Should().BeGreaterThan((short)4);
        ((byte)5).Should().BeGreaterThan((byte)4);

        Fails(() => ((uint)3).Should().BeGreaterThan(4u));
    }

    [Fact]
    public void HaveFlag_DiffersFromEquality()
    {
        const AttributeTargets both = AttributeTargets.Class | AttributeTargets.Struct;

        both.Should().HaveFlag(AttributeTargets.Class);
        both.Should().HaveFlag(AttributeTargets.Struct);
        both.Should().NotHaveFlag(AttributeTargets.Method);

        // The distinction this exists for: it has the flag, and equals neither of them alone.
        Fails(() => both.Should().Be(AttributeTargets.Class));
        Fails(() => both.Should().NotHaveFlag(AttributeTargets.Class));
    }

    [Fact]
    public void Guid_EmptyMeansTheZeroGuid_NotALengthOfZero()
    {
        Guid.Empty.Should().BeEmpty();
        Guid.NewGuid().Should().NotBeEmpty();

        Fails(() => Guid.NewGuid().Should().BeEmpty());
        Fails(() => Guid.Empty.Should().NotBeEmpty());
        // A null guid is not an empty one, and cannot pass the "not empty" check either.
        Fails(() => ((Guid?)null).Should().NotBeEmpty());
    }

    [Fact]
    public void Guid_ComparesByValue()
    {
        var id = Guid.NewGuid();

        id.Should().Be(id);
        id.Should().NotBe(Guid.NewGuid());
        Fails(() => id.Should().Be(Guid.NewGuid()));
    }
}
