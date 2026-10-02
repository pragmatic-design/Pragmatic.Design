using Pragmatic.Testing.Assertions;

using static Pragmatic.Testing.Tests.Assertions.AssertionProbe;

namespace Pragmatic.Testing.Tests.Assertions;

/// <summary>Every collection assertion, in both directions.</summary>
public class CollectionAssertionTests
{
    private static readonly int[] Numbers = [1, 2, 3];

    [Fact]
    public void HaveCountGreaterThan_IsStrict()
    {
        Numbers.Should().HaveCountGreaterThan(2);
        Fails(() => Numbers.Should().HaveCountGreaterThan(3));
    }

    [Fact]
    public void HaveCountGreaterOrEqualTo_IsNot()
    {
        Numbers.Should().HaveCountGreaterOrEqualTo(3);
        Fails(() => Numbers.Should().HaveCountGreaterOrEqualTo(4));
    }

    [Fact]
    public void HaveCountLessThan_IsStrict()
    {
        Numbers.Should().HaveCountLessThan(4);
        Fails(() => Numbers.Should().HaveCountLessThan(3));
    }

    [Fact]
    public void NotBeNullOrEmpty_RejectsBoth()
    {
        Numbers.Should().NotBeNullOrEmpty();
        Fails(() => Array.Empty<int>().Should().NotBeNullOrEmpty());
        Fails(() => ((int[]?)null).Should().NotBeNullOrEmpty());
    }

    [Fact]
    public void Contain_WithASequence_RequiresEveryElement()
    {
        Numbers.Should().Contain([1, 3]);
        Fails(() => Numbers.Should().Contain([1, 9]));
    }

    [Fact]
    public void NotContain_WithAPredicate_RejectsAnyMatch()
    {
        Numbers.Should().NotContain(n => n > 5);
        Fails(() => Numbers.Should().NotContain(n => n > 2));
    }

    [Fact]
    public void OnlyContain_RequiresEveryElementToMatch()
    {
        Numbers.Should().OnlyContain(n => n > 0);
        Fails(() => Numbers.Should().OnlyContain(n => n > 1));
    }

    [Fact]
    public void OnlyHaveUniqueItems_FindsDuplicates()
    {
        Numbers.Should().OnlyHaveUniqueItems();
        Fails(() => new[] { 1, 1, 2 }.Should().OnlyHaveUniqueItems());
    }

    [Fact]
    public void AllSatisfy_RunsTheAssertionOnEveryElement()
    {
        Numbers.Should().AllSatisfy(n => n.Should().BeGreaterThan(0));
        Fails(() => Numbers.Should().AllSatisfy(n => n.Should().BeGreaterThan(1)));
    }

    [Fact]
    public void AllBeOfType_RequiresEveryElementToBeExactlyThatType()
    {
        object[] all = ["a", "b"];
        all.Should().AllBeOfType<string>();

        object[] mixed = ["a", 1];
        Fails(() => mixed.Should().AllBeOfType<string>());
    }

    [Fact]
    public void AllBeEquivalentTo_RequiresEveryElementToEqualTheSameValue()
    {
        new[] { 7, 7, 7 }.Should().AllBeEquivalentTo(7);
        Fails(() => new[] { 7, 7, 8 }.Should().AllBeEquivalentTo(7));
    }

    /// <summary>
    ///     Relative order, not adjacency: the elements must appear in that sequence, with anything
    ///     allowed between them.
    /// </summary>
    [Fact]
    public void ContainInOrder_ChecksRelativeOrder()
    {
        new[] { 1, 5, 2, 6, 3 }.Should().ContainInOrder(1, 2, 3);
        Fails(() => Numbers.Should().ContainInOrder(3, 1));
    }

    [Fact]
    public void NotEqual_ComparesSequences()
    {
        Numbers.Should().NotEqual([3, 2, 1]);
        Fails(() => Numbers.Should().NotEqual([1, 2, 3]));
    }

    [Fact]
    public void BeSubsetOf_AllowsMissingButNotExtra()
    {
        new[] { 1, 3 }.Should().BeSubsetOf(Numbers);
        Numbers.Should().BeSubsetOf(Numbers);
        Fails(() => new[] { 1, 9 }.Should().BeSubsetOf(Numbers));
    }

    [Fact]
    public void NotIntersectWith_RequiresNoSharedElement()
    {
        Numbers.Should().NotIntersectWith([7, 8]);
        Fails(() => Numbers.Should().NotIntersectWith([3, 8]));
    }

    [Fact]
    public void BeEquivalentTo_IgnoresOrder_ButNotMultiplicity()
    {
        Numbers.Should().BeEquivalentTo([3, 1, 2]);
        Fails(() => Numbers.Should().BeEquivalentTo([1, 2]));
        Fails(() => Numbers.Should().BeEquivalentTo([1, 2, 3, 3]));

        // Same elements, different counts: {1,1,2} is not equivalent to {1,2,2}.
        Fails(() => new[] { 1, 1, 2 }.Should().BeEquivalentTo([1, 2, 2]));
    }

    [Fact]
    public void NotBeEquivalentTo_IsTheOpposite()
    {
        Numbers.Should().NotBeEquivalentTo([1, 2]);
        Fails(() => Numbers.Should().NotBeEquivalentTo([3, 2, 1]));
    }

    [Fact]
    public void BeEquivalentTo_WithStrictOrdering_BecomesEqual()
    {
        Numbers.Should().BeEquivalentTo([1, 2, 3], options => options.WithStrictOrdering());
        Fails(() => Numbers.Should().BeEquivalentTo([3, 2, 1], options => options.WithStrictOrdering()));

        // And without it, order is free again — the default, spelled out.
        Numbers.Should().BeEquivalentTo([3, 2, 1], options => options.WithoutStrictOrdering());
    }

    [Fact]
    public void StartWith_AndEndWith_CheckThePrefixAndSuffix()
    {
        Numbers.Should().StartWith([1, 2]);
        Numbers.Should().StartWith(1);
        Numbers.Should().EndWith([2, 3]);

        Fails(() => Numbers.Should().StartWith([2, 3]));
        Fails(() => Numbers.Should().EndWith([1, 2]));
        // A prefix longer than the sequence cannot match.
        Fails(() => Numbers.Should().StartWith([1, 2, 3, 4]));
    }

    [Fact]
    public void BeInAscendingOrder_AndDescending_AreOpposites()
    {
        Numbers.Should().BeInAscendingOrder();
        new[] { 3, 2, 1 }.Should().BeInDescendingOrder();

        Fails(() => new[] { 3, 2, 1 }.Should().BeInAscendingOrder());
        Fails(() => Numbers.Should().BeInDescendingOrder());
    }

    [Fact]
    public void BeInOrder_WithASelector_OrdersByTheKey()
    {
        var people = new[] { (Name: "b", Age: 1), (Name: "a", Age: 2) };

        people.Should().BeInAscendingOrder(p => p.Age);
        people.Should().BeInDescendingOrder(p => p.Name);

        Fails(() => people.Should().BeInAscendingOrder(p => p.Name));
    }

    /// <summary>Equal elements are in order, whichever direction is asked for.</summary>
    [Fact]
    public void Ordering_AcceptsEqualNeighbours()
    {
        new[] { 1, 1, 2 }.Should().BeInAscendingOrder();
        new[] { 2, 1, 1 }.Should().BeInDescendingOrder();
    }

    [Fact]
    public void ContainSingle_WithAPredicate_RequiresExactlyOneMatch()
    {
        Numbers.Should().ContainSingle(n => n == 2).Which.Should().Be(2);
        Fails(() => Numbers.Should().ContainSingle(n => n > 1));
        Fails(() => Numbers.Should().ContainSingle(n => n > 9));
    }

    /// <summary>A null sequence is empty for counting, but not for the null checks.</summary>
    [Fact]
    public void NullSequence_IsHandledWithoutThrowing()
    {
        int[]? missing = null;

        missing.Should().BeNull();
        Fails(() => missing.Should().HaveCount(1));
        Fails(() => missing.Should().NotBeNull());
    }
}
