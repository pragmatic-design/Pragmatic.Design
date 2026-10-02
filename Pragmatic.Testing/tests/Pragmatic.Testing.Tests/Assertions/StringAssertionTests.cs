using Pragmatic.Testing.Assertions;

using static Pragmatic.Testing.Tests.Assertions.AssertionProbe;

namespace Pragmatic.Testing.Tests.Assertions;

/// <summary>Every string assertion, in both directions.</summary>
public class StringAssertionTests
{
    [Fact]
    public void StartWith_AndNotStartWith_AreOpposites()
    {
        "hello".Should().StartWith("hel");
        "hello".Should().NotStartWith("ell");
        Fails(() => "hello".Should().StartWith("ell"));
        Fails(() => "hello".Should().NotStartWith("hel"));
    }

    [Fact]
    public void EndWith_AndNotEndWith_AreOpposites()
    {
        "hello".Should().EndWith("llo");
        "hello".Should().NotEndWith("hel");
        Fails(() => "hello".Should().EndWith("hel"));
        Fails(() => "hello".Should().NotEndWith("llo"));
    }

    [Fact]
    public void Contain_IsCaseSensitive_AndContainEquivalentOf_IsNot()
    {
        "Hello".Should().Contain("ell");
        Fails(() => "Hello".Should().Contain("ELL"));

        "Hello".Should().ContainEquivalentOf("ELL");
        Fails(() => "Hello".Should().ContainEquivalentOf("xyz"));
    }

    [Fact]
    public void BeEmpty_DistinguishesEmptyFromNull()
    {
        "".Should().BeEmpty();
        "x".Should().NotBeEmpty();
        Fails(() => "x".Should().BeEmpty());

        // Null is not empty, and the two checks below are the ones that say so.
        Fails(() => ((string?)null).Should().BeEmpty());
    }

    [Fact]
    public void BeNullOrEmpty_AcceptsBoth()
    {
        ((string?)null).Should().BeNullOrEmpty();
        "".Should().BeNullOrEmpty();
        "x".Should().NotBeNullOrEmpty();

        Fails(() => "x".Should().BeNullOrEmpty());
        Fails(() => "".Should().NotBeNullOrEmpty());
        Fails(() => ((string?)null).Should().NotBeNullOrEmpty());
    }

    [Fact]
    public void BeNullOrWhiteSpace_TreatsSpacesAsEmpty()
    {
        "   ".Should().BeNullOrWhiteSpace();
        ((string?)null).Should().BeNullOrWhiteSpace();
        "x".Should().NotBeNullOrWhiteSpace();

        Fails(() => "x".Should().BeNullOrWhiteSpace());
        Fails(() => "   ".Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void HaveLength_CountsCharacters()
    {
        "abc".Should().HaveLength(3);
        Fails(() => "abc".Should().HaveLength(2));
        Fails(() => ((string?)null).Should().HaveLength(0));
    }

    [Fact]
    public void BeEquivalentTo_OnStrings_IgnoresCase()
    {
        "Hello".Should().BeEquivalentTo("hello");
        Fails(() => "Hello".Should().BeEquivalentTo("hell"));
    }

    [Fact]
    public void MatchRegex_AndNotMatchRegex_AreOpposites()
    {
        "abc123".Should().MatchRegex(@"^[a-z]+\d+$");
        "abc123".Should().NotMatchRegex(@"^\d+$");
        Fails(() => "abc123".Should().MatchRegex(@"^\d+$"));
        Fails(() => "abc123".Should().NotMatchRegex(@"^[a-z]+\d+$"));
    }

    /// <summary>
    ///     Wildcards, not a regex — <c>*</c> stands for anything, and the pattern is anchored at
    ///     both ends. It is the shape <c>WithMessage</c> uses, so it carries most of the exception
    ///     assertions in the repository.
    /// </summary>
    [Fact]
    public void Match_UsesWildcards_AnchoredAtBothEnds()
    {
        "size exceeds the limit".Should().Match("*exceeds*");
        "size exceeds the limit".Should().Match("size*limit");
        "size exceeds the limit".Should().Match("*");

        // Anchored: a pattern without a leading star must match from the start.
        Fails(() => "size exceeds the limit".Should().Match("exceeds*"));
        Fails(() => "size exceeds the limit".Should().Match("*under*"));
    }

    [Fact]
    public void Match_IsCaseInsensitive_LikeTheMessagesItChecks()
    {
        "Size Exceeds".Should().Match("*exceeds*");
    }

    /// <summary>
    ///     A string is an <c>IEnumerable&lt;char&gt;</c>. Without the string overload outranking the
    ///     sequence one, every <c>Contain("text")</c> here would be asking for a single character.
    /// </summary>
    [Fact]
    public void StringBindsToTheStringFamily_NotTheSequenceOne()
    {
        "hello".Should().Should().BeOfType<StringAssertions>();
    }
}
