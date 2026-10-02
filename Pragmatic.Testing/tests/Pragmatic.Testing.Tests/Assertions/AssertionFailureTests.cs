using Pragmatic.Testing.Assertions;

namespace Pragmatic.Testing.Tests.Assertions;

/// <summary>
///     Every assertion has to fail when it should. A check that never fails is worse than no check:
///     it reports a guarantee nobody is holding, and 20,000 call sites rest on these.
/// </summary>
/// <remarks>
///     Each test asserts both directions — the passing case does not throw, the failing case does.
///     Testing only the passing case is exactly how an assertion that asserts nothing goes unnoticed.
/// </remarks>
public class AssertionFailureTests
{
    private static PragmaticTestAssertionException Fails(Action assertion)
    {
        try
        {
            assertion();
        }
        catch (PragmaticTestAssertionException expected)
        {
            return expected;
        }

        throw new PragmaticTestAssertionException(
            "the assertion was expected to fail, and did not — it is not checking anything");
    }

    [Fact]
    public void Be_ComparesByEquality()
    {
        42.Should().Be(42);
        Fails(() => 42.Should().Be(43));
    }

    [Fact]
    public void Be_OnStrings_IsOrdinal()
    {
        "abc".Should().Be("abc");
        Fails(() => "abc".Should().Be("ABC"));
    }

    [Fact]
    public void BeTrue_AndBeFalse_RejectTheOtherValue()
    {
        true.Should().BeTrue();
        false.Should().BeFalse();
        Fails(() => false.Should().BeTrue());
        Fails(() => true.Should().BeFalse());
    }

    [Fact]
    public void BeNull_AndNotBeNull_AreOpposites()
    {
        ((string?)null).Should().BeNull();
        "x".Should().NotBeNull();
        Fails(() => "x".Should().BeNull());
        Fails(() => ((string?)null).Should().NotBeNull());
    }

    [Fact]
    public void Contain_OnString_LooksInsideIt()
    {
        "hello world".Should().Contain("lo wo");
        Fails(() => "hello world".Should().Contain("nope"));
    }

    [Fact]
    public void Contain_OnCollection_LooksForTheElement()
    {
        new[] { 1, 2, 3 }.Should().Contain(2);
        Fails(() => new[] { 1, 2, 3 }.Should().Contain(9));
    }

    [Fact]
    public void HaveCount_CountsTheElements()
    {
        new[] { 1, 2, 3 }.Should().HaveCount(3);
        Fails(() => new[] { 1, 2, 3 }.Should().HaveCount(2));
    }

    [Fact]
    public void BeEmpty_AndNotBeEmpty_AreOpposites()
    {
        Array.Empty<int>().Should().BeEmpty();
        new[] { 1 }.Should().NotBeEmpty();
        Fails(() => new[] { 1 }.Should().BeEmpty());
        Fails(() => Array.Empty<int>().Should().NotBeEmpty());
    }

    [Fact]
    public void ContainSingle_RequiresExactlyOne()
    {
        new[] { 7 }.Should().ContainSingle().Which.Should().Be(7);
        Fails(() => new[] { 1, 2 }.Should().ContainSingle());
        Fails(() => Array.Empty<int>().Should().ContainSingle());
    }

    [Fact]
    public void Equal_ComparesElementsInOrder()
    {
        new[] { 1, 2 }.Should().Equal(1, 2);
        Fails(() => new[] { 1, 2 }.Should().Equal(2, 1));
        Fails(() => new[] { 1, 2 }.Should().Equal(1));
    }

    [Fact]
    public void BeOfType_IsExact_NotAssignable()
    {
        object subject = "text";
        subject.Should().BeOfType<string>();
        Fails(() => subject.Should().BeOfType<object>());

        // Assignable is the one that accepts a base type.
        subject.Should().BeAssignableTo<object>();
    }

    [Fact]
    public void BeGreaterThan_OrdersNumbers()
    {
        5.Should().BeGreaterThan(4);
        Fails(() => 5.Should().BeGreaterThan(5));
        Fails(() => 4.Should().BeGreaterThan(5));
    }

    [Fact]
    public void ContainKey_FindsTheEntry()
    {
        var map = new Dictionary<string, int> { ["a"] = 1 };
        map.Should().ContainKey("a").Which.Should().Be(1);
        Fails(() => map.Should().ContainKey("b"));
    }

    [Fact]
    public void Throw_RequiresTheException()
    {
        Action throwing = () => throw new InvalidOperationException("boom");
        throwing.Should().Throw<InvalidOperationException>();

        // Wrong type, and no exception at all: both must fail.
        Fails(() => throwing.Should().Throw<ArgumentException>());
        Fails(() => ((Action)(() => { })).Should().Throw<Exception>());
    }

    [Fact]
    public void WithMessage_MatchesWildcards()
    {
        Action throwing = () => throw new InvalidOperationException("size exceeds the limit");
        throwing.Should().Throw<InvalidOperationException>().WithMessage("*exceeds*");
        Fails(() => throwing.Should().Throw<InvalidOperationException>().WithMessage("*under*"));
    }

    [Fact]
    public void NotThrow_RejectsAnyException()
    {
        ((Action)(() => { })).Should().NotThrow();
        Fails(() => ((Action)(() => throw new InvalidOperationException())).Should().NotThrow());
    }

    [Fact]
    public async Task ThrowAsync_AwaitsTheDelegate()
    {
        Func<Task> throwing = () => Task.FromException(new InvalidOperationException("late"));
        await throwing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*late*");

        // A task that faults must not be reported as "did not throw" merely because nothing was
        // observed synchronously.
        Fails(() => throwing.Should().ThrowAsync<ArgumentException>().GetAwaiter().GetResult());
    }

    [Fact]
    public async Task NotThrowAsync_RejectsAFaultedTask()
    {
        await ((Func<Task>)(() => Task.CompletedTask)).Should().NotThrowAsync();
        Fails(() => ((Func<Task>)(() => Task.FromException(new InvalidOperationException())))
            .Should().NotThrowAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public void And_ContinuesOnTheSameFamily()
    {
        // The point of the chain: after HaveCount the string vocabulary is still reachable, which
        // only holds because the constraint carries the derived assertion type.
        new[] { "a", "b" }.Should().HaveCount(2).And.Contain("a");
        Fails(() => new[] { "a", "b" }.Should().HaveCount(2).And.Contain("z"));
    }

    [Fact]
    public void Which_CarriesTheSelectedValue()
    {
        var items = new[] { new { Name = "x" } };
        items.Should().ContainSingle().Which.Name.Should().Be("x");
        Fails(() => items.Should().ContainSingle().Which.Name.Should().Be("y"));
    }

    [Fact]
    public void BeInAscendingOrder_NamesTheOffendingPair()
    {
        new[] { 1, 2, 3 }.Should().BeInAscendingOrder();

        var failure = Fails(() => new[] { 1, 3, 2 }.Should().BeInAscendingOrder());
        failure.Message.Should().Contain("3").And.Contain("2");
    }

    [Fact]
    public void FailureMessage_NamesTheCallersExpression()
    {
        var order = new { Total = 10 };

        var failure = Fails(() => order.Total.Should().Be(11));

        // CallerArgumentExpression, not a parsed stack trace: the message says what was written.
        failure.Message.Should().Contain("order.Total");
    }

    [Fact]
    public void Because_ReadsTheSameWithOrWithoutTheWord()
    {
        Fails(() => 1.Should().Be(2, "because it is cached")).Message
            .Should().Contain("because it is cached").And.NotContain("because because");

        Fails(() => 1.Should().Be(2, "it is cached")).Message
            .Should().Contain("because it is cached");
    }

    private readonly record struct Money(decimal Amount);

    /// <summary>
    ///     <c>default</c> takes its type from the parameter it is passed to. On the object fallback
    ///     it would become null, so <c>result.Should().Be(default)</c> would ask whether a struct is
    ///     null — always false, and three Temporal tests said so.
    /// </summary>
    [Fact]
    public void Be_Default_MeansTheStructsDefault_NotNull()
    {
        default(Money).Should().Be(default);
        Fails(() => new Money(5).Should().Be(default));
    }

    /// <summary>
    ///     Two boxed numbers of different types are not equal, and the assertion says so.
    /// </summary>
    /// <remarks>
    ///     Deliberately not the behaviour of the library this replaced, which compared numbers across
    ///     types. That convenience kept <c>Parse_IntLiteral</c> green while the parser it tested
    ///     returned a <c>double</c> for every literal — the assertion could not tell the difference,
    ///     so nobody could. A failure reading "Expected 42, but found 42" is a poor message about a
    ///     real defect, and the message is the cheaper of the two things to fix.
    /// </remarks>
    [Fact]
    public void Be_DoesNotEquateNumbersOfDifferentTypes()
    {
        object stored = 42L;
        stored.Should().Be(42L);
        Fails(() => stored.Should().Be(42));

        object parsed = 42.0d;
        parsed.Should().Be(42.0d);
        Fails(() => parsed.Should().Be(42));

        object text = "42";
        Fails(() => text.Should().Be(42));
    }

    /// <summary>
    ///     On a <see cref="Type"/> the question is about the type it describes, not about the
    ///     <c>Type</c> object — which is a <c>RuntimeType</c> and implements none of these.
    /// </summary>
    [Fact]
    public void BeAssignableTo_OnAType_AsksAboutThatType()
    {
        typeof(List<int>).Should().BeAssignableTo<IEnumerable<int>>();
        typeof(List<int>).Should().Implement<IEnumerable<int>>();
        Fails(() => typeof(List<int>).Should().BeAssignableTo<IDisposable>());
    }

    [Fact]
    public void CollectionIsEnumeratedOnce()
    {
        var enumerations = 0;

        IEnumerable<int> Counting()
        {
            enumerations++;
            yield return 1;
        }

        Counting().Should().HaveCount(1).And.Contain(1).And.NotBeEmpty();

        // Three assertions, one enumeration: a query with side effects — or one over a mock, where
        // each pass counts as another call — must not be re-run by the chain.
        enumerations.Should().Be(1);
    }
}
