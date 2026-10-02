using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     <c>default(EquatableArray&lt;T&gt;)</c> and <see cref="EquatableArray{T}.Empty" /> are both
///     "no elements" everywhere else in the type (Count, enumerator, AsImmutableArray). If equality
///     told them apart, a transform that leaves a collection unset and one that sets it to empty would
///     produce models that compare unequal — a cache miss the incremental pipeline cannot see.
/// </summary>
public sealed class EquatableArrayTests
{
    private static EquatableArray<string> Default => default;
    private static EquatableArray<string> Empty => EquatableArray<string>.Empty;
    private static EquatableArray<string> FromEmptyImmutable => new(ImmutableArray<string>.Empty);
    private static EquatableArray<string> NonEmpty => new(ImmutableArray.Create("a"));

    [Fact]
    public void Equals_DefaultVsDefault_IsTrue()
    {
        Default.Equals(Default).Should().BeTrue();
        (Default == Default).Should().BeTrue();
        Default.GetHashCode().Should().Be(Default.GetHashCode());
    }

    [Fact]
    public void Equals_DefaultVsEmpty_IsTrue()
    {
        Default.Equals(Empty).Should().BeTrue();
        Empty.Equals(Default).Should().BeTrue();
        (Default == Empty).Should().BeTrue();
        (Default != Empty).Should().BeFalse();
        Default.GetHashCode().Should().Be(Empty.GetHashCode());
    }

    [Fact]
    public void Equals_EmptyVsEmpty_IsTrue()
    {
        Empty.Equals(FromEmptyImmutable).Should().BeTrue();
        Empty.GetHashCode().Should().Be(FromEmptyImmutable.GetHashCode());
    }

    [Fact]
    public void Equals_DefaultVsNonEmpty_IsFalse()
    {
        Default.Equals(NonEmpty).Should().BeFalse();
        NonEmpty.Equals(Default).Should().BeFalse();
        (Default == NonEmpty).Should().BeFalse();
    }

    [Fact]
    public void Equals_EmptyVsNonEmpty_IsFalse()
    {
        Empty.Equals(NonEmpty).Should().BeFalse();
        NonEmpty.Equals(Empty).Should().BeFalse();
    }

    [Fact]
    public void Equals_SameContent_IsTrue()
    {
        var left = new EquatableArray<string>(ImmutableArray.Create("a", "b"));
        var right = new EquatableArray<string>(ImmutableArray.Create("a", "b"));

        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentContentOrOrder_IsFalse()
    {
        var ab = new EquatableArray<string>(ImmutableArray.Create("a", "b"));
        var ba = new EquatableArray<string>(ImmutableArray.Create("b", "a"));
        var abc = new EquatableArray<string>(ImmutableArray.Create("a", "b", "c"));

        ab.Equals(ba).Should().BeFalse();
        ab.Equals(abc).Should().BeFalse();
    }

    [Fact]
    public void EqualsObject_DefaultVsBoxedEmpty_IsTrue()
        => Default.Equals((object)Empty).Should().BeTrue();

    [Fact]
    public void Default_BehavesAsEmptyEverywhere()
    {
        Default.Count.Should().Be(0);
        Default.Length.Should().Be(0);
        Default.IsDefaultOrEmpty.Should().BeTrue();
        Default.AsImmutableArray().Should().BeEmpty();
        Default.AsEnumerable().ToList().Should().BeEmpty(); // enumerating the default must not throw
    }

    /// <summary>
    ///     The scenario the default-as-empty behaviour exists for: a record model whose collection is left unset must equal one
    ///     whose collection was explicitly set to empty, or the pipeline re-runs the downstream stage.
    /// </summary>
    [Fact]
    public void RecordModel_UnsetVsExplicitlyEmptyCollection_AreEqual()
    {
        var unset = new Model("X");
        var explicitlyEmpty = new Model("X") { Items = EquatableArray<string>.Empty };

        explicitlyEmpty.Should().Be(unset);
        explicitlyEmpty.GetHashCode().Should().Be(unset.GetHashCode());
    }

    private sealed record Model(string Name)
    {
        public EquatableArray<string> Items { get; init; }
    }
}
