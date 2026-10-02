using Pragmatic.Testing.Assertions;

using static Pragmatic.Testing.Tests.Assertions.AssertionProbe;

namespace Pragmatic.Testing.Tests.Assertions;

/// <summary>The assertions every family inherits, plus the dictionary and type ones.</summary>
public class SubjectAssertionTests
{
    [Fact]
    public void NotBe_IsTheOppositeOfBe()
    {
        42.Should().NotBe(43);
        Fails(() => 42.Should().NotBe(42));
    }

    /// <summary>
    ///     Identity, not equality. Two equal-but-distinct instances are the case that tells the two
    ///     checks apart, so it is the one asserted.
    /// </summary>
    [Fact]
    public void BeSameAs_IsIdentity_NotEquality()
    {
        var first = new List<int> { 1 };
        var second = new List<int> { 1 };
        var alias = first;

        first.Should().BeSameAs(alias);
        first.Should().NotBeSameAs(second);

        Fails(() => first.Should().BeSameAs(second));
        Fails(() => first.Should().NotBeSameAs(alias));
    }

    [Fact]
    public void BeOneOf_AcceptsAnyOfTheCandidates()
    {
        2.Should().BeOneOf(1, 2, 3);
        2.Should().BeOneOf([1, 2, 3], "it is one of the allowed values");
        Fails(() => 9.Should().BeOneOf(1, 2, 3));
    }

    [Fact]
    public void Match_WithAPredicate_AndWithAType()
    {
        object subject = "hello";

        subject.Should().Match(s => s is string);
        subject.Should().Match<string>(s => s.StartsWith("hel"));

        Fails(() => subject.Should().Match<string>(s => s.StartsWith("xyz")));
        // Wrong type: the cast fails before the predicate is ever reached.
        Fails(() => subject.Should().Match<Uri>(u => true));
    }

    [Fact]
    public void Satisfy_RunsTheBody_AndPropagatesItsFailure()
    {
        42.Should().Satisfy(n => n.Should().BeGreaterThan(0));
        Fails(() => 42.Should().Satisfy(n => n.Should().BeGreaterThan(100)));
    }

    [Fact]
    public void NotBeOfType_IsTheOppositeOfBeOfType()
    {
        object subject = "text";

        subject.Should().NotBeOfType<Uri>();
        Fails(() => subject.Should().NotBeOfType<string>());
    }

    [Fact]
    public void BeOfType_WithARuntimeType()
    {
        object subject = "text";

        subject.Should().BeOfType(typeof(string));
        Fails(() => subject.Should().BeOfType(typeof(Uri)));
    }

    [Fact]
    public void BeAssignableTo_WithARuntimeType()
    {
        object subject = new List<int>();

        subject.Should().BeAssignableTo(typeof(IEnumerable<int>));
        Fails(() => subject.Should().BeAssignableTo(typeof(IDisposable)));
    }

    [Fact]
    public void ContainKeys_RequiresEveryKey()
    {
        var map = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        map.Should().ContainKeys("a", "b");
        Fails(() => map.Should().ContainKeys("a", "z"));
    }

    [Fact]
    public void NotContainKey_IsTheOpposite()
    {
        var map = new Dictionary<string, int> { ["a"] = 1 };

        map.Should().NotContainKey("z");
        Fails(() => map.Should().NotContainKey("a"));
    }

    [Fact]
    public void Contain_OnADictionary_ChecksTheMapping()
    {
        var map = new Dictionary<string, int> { ["a"] = 1 };

        map.Should().Contain("a", 1);
        Fails(() => map.Should().Contain("a", 2));
        Fails(() => map.Should().Contain("z", 1));
    }

    [Fact]
    public void Dictionary_CountAndEmptiness()
    {
        var map = new Dictionary<string, int> { ["a"] = 1 };

        map.Should().HaveCount(1).And.NotBeEmpty();
        new Dictionary<string, int>().Should().BeEmpty();

        Fails(() => map.Should().HaveCount(2));
        Fails(() => map.Should().BeEmpty());
        Fails(() => new Dictionary<string, int>().Should().NotBeEmpty());
    }

    [Fact]
    public void ContainSingle_OnADictionary_CarriesTheEntry()
    {
        var map = new Dictionary<string, int> { ["a"] = 1 };

        map.Should().ContainSingle().Which.Key.Should().Be("a");
        Fails(() => new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }.Should().ContainSingle());
    }

    /// <summary>
    ///     A concrete dictionary matches the read-only and the mutable overload equally well, so
    ///     without an explicit ranking every call here is ambiguous and nothing compiles.
    /// </summary>
    [Fact]
    public void Dictionary_BindsToTheDictionaryFamily()
    {
        IDictionary<string, int> mutable = new Dictionary<string, int> { ["a"] = 1 };
        IReadOnlyDictionary<string, int> readOnly = new Dictionary<string, int> { ["a"] = 1 };

        mutable.Should().ContainKey("a");
        readOnly.Should().ContainKey("a");
    }

    [Fact]
    public void WhoseValue_IsTheDictionarySpellingOfWhich()
    {
        var map = new Dictionary<string, string> { ["a"] = "one" };

        map.Should().ContainKey("a").WhoseValue.Should().Be("one");
    }

    [Fact]
    public void BeDerivedFrom_IsStrict_AndImplementIsNot()
    {
        typeof(ArgumentNullException).Should().BeDerivedFrom<ArgumentException>();
        typeof(List<int>).Should().Implement<IEnumerable<int>>();
        typeof(List<int>).Should().NotImplement<IDisposable>();

        // A type does not derive from itself.
        Fails(() => typeof(ArgumentException).Should().BeDerivedFrom<ArgumentException>());
        Fails(() => typeof(List<int>).Should().Implement<IDisposable>());
        Fails(() => typeof(List<int>).Should().NotImplement<IEnumerable<int>>());
    }

    [Fact]
    public void Be_OnAType_ComparesTheTypeItself()
    {
        typeof(string).Should().Be<string>();
        typeof(string).Should().Be(typeof(string));
        Fails(() => typeof(string).Should().Be<Uri>());
    }

    /// <summary>An empty indexer-only or event-only surface still has to be reachable.</summary>
    [Fact]
    public void Format_RendersValuesForTheMessage()
    {
        AssertionFailure.Format(null).Should().Be("<null>");
        AssertionFailure.Format("x").Should().Be("\"x\"");
        AssertionFailure.Format(true).Should().Be("True");
        AssertionFailure.Format(new[] { 1, 2 }).Should().Be("{1, 2}");

        // A long collection is elided rather than filling the message.
        AssertionFailure.Format(Enumerable.Range(1, 20).ToArray()).Should().EndWith("…}");
    }
}
