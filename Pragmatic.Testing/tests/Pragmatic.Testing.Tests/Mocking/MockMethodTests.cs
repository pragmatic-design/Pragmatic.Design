using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Xunit;

namespace Pragmatic.Testing.Tests.Mocking;

public sealed class MockMethodTests
{
    [Fact]
    public void Invoke_NotConfigured_ReturnsDefaultAndCounts()
    {
        var method = new MockMethod<string, int>("IThing.Count");

        method.Invoke("k").Should().Be(0);
        method.CallCount.Should().Be(1);
        method.Calls.Should().Equal("k");
    }

    [Fact]
    public void Returns_Factory_SeesTheArguments()
    {
        var method = new MockMethod<int, int, int>().Returns((a, b) => a + b);

        method.Invoke(2, 3).Should().Be(5);
    }

    [Fact]
    public void Throws_Configured_ThrowsAndStillRecordsTheCall()
    {
        var method = new MockMethod<string, int>().Throws(new InvalidOperationException("boom"));

        method.Invoking(m => m.Invoke("k")).Should().Throw<InvalidOperationException>();
        method.CallCount.Should().Be(1);
    }

    [Fact]
    public void Received_WithMatcher_CountsOnlyMatchingCalls()
    {
        var method = new MockMethod<string, int>("ICache.Get");
        method.Invoke("a");
        method.Invoke("b");
        method.Invoke("a");

        method.Invoking(m => m.Received(3)).Should().NotThrow();
        method.Invoking(m => m.Received(2, "a")).Should().NotThrow();
        method.Invoking(m => m.Received(1, "b")).Should().NotThrow();
    }

    [Fact]
    public void Received_WithPredicateMatcher_Filters()
    {
        var method = new MockMethod<string, int>();
        method.Invoke("key:1");
        method.Invoke("other");

        method.Invoking(m => m.Received(1, Arg.Is<string>(k => k.StartsWith("key"))))
            .Should().NotThrow();
    }

    // A matcher that matches nothing must fail, not silently pass.
    [Fact]
    public void Received_MatcherThatMatchesNothing_Throws()
    {
        var method = new MockMethod<string, int>("ICache.Get");
        method.Invoke("a");

        method.Invoking(m => m.Received(1, "zzz"))
            .Should().Throw<PragmaticTestAssertionException>();
    }

    [Fact]
    public void Received_ConstrainedMismatch_MessageSaysOtherCallsExist()
    {
        var method = new MockMethod<string, int>("ICache.Get");
        method.Invoke("a");
        method.Invoke("b");

        method.Invoking(m => m.Received(1, "zzz"))
            .Should().Throw<PragmaticTestAssertionException>()
            .WithMessage("*ICache.Get*\"zzz\"*2 call(s) to it in total*");
    }

    [Fact]
    public void OmittedMatchers_MatchAnything()
    {
        var method = new MockMethod<string, int, bool>("IThing.Do");
        method.Invoke("a", 1);
        method.Invoke("a", 2);

        // second matcher omitted → any
        method.Invoking(m => m.Received(2, "a")).Should().NotThrow();
        method.Invoking(m => m.Received(1, "a", 2)).Should().NotThrow();
    }

    [Fact]
    public void DidNotReceive_WhenCalled_Throws()
    {
        var method = new MockMethod<int>("IClock.GetTimeProvider");
        method.Invoke();

        method.Invoking(m => m.DidNotReceive())
            .Should().Throw<PragmaticTestAssertionException>();
    }

    [Fact]
    public void ClearReceivedCalls_ForgetsCallsButKeepsConfiguration()
    {
        var method = new MockMethod<string, int>().Returns(7);
        method.Invoke("a");

        method.ClearReceivedCalls();

        method.CallCount.Should().Be(0);
        method.Invoke("b").Should().Be(7);
    }

    [Fact]
    public void FourArguments_RecordAndMatch()
    {
        var method = new MockMethod<string, int, bool, char, string>("IThing.Wide");
        method.Invoke("a", 1, true, 'x');

        method.Invoking(m => m.Received(1, "a", 1, true, 'x')).Should().NotThrow();
        method.Invoking(m => m.Received(1, "a", 2)).Should().Throw<PragmaticTestAssertionException>();
    }

    [Fact]
    public void VoidMethod_DoesCallbackRunsAndCallsAreCounted()
    {
        var seen = new List<string>();
        var method = new MockVoidMethod<string>("IUnitOfWork.Add").Does(seen.Add);

        method.Invoke("a");
        method.Invoke("b");

        seen.Should().Equal("a", "b");
        method.Invoking(m => m.Received(2)).Should().NotThrow();
        method.Invoking(m => m.Received(1, "a")).Should().NotThrow();
    }
}
