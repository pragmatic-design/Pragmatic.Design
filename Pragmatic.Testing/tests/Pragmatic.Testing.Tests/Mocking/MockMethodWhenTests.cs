using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Xunit;

namespace Pragmatic.Testing.Tests.Mocking;

/// <summary>
///     Conditional configuration. This exists because tests routinely configure one method to answer
///     differently per argument — a password verifier saying true for the right password and false
///     for the wrong one, in the same test. Without it those collapse into one unconditional result,
///     the last one wins, and the test keeps passing while it stops checking anything.
/// </summary>
public sealed class MockMethodWhenTests
{
    [Fact]
    public void TwoArguments_TwoAnswers()
    {
        var verify = new MockMethod<string, string, bool>("IPasswordHasher.Verify");
        verify.When("correct-password", "hash").Returns(true);
        verify.When("wrong-password", "hash").Returns(false);

        verify.Invoke("correct-password", "hash").Should().BeTrue();
        verify.Invoke("wrong-password", "hash").Should().BeFalse();
    }

    [Fact]
    public void UnmatchedCall_FallsBackToTheUnconditionalResult()
    {
        var hash = new MockMethod<string, string>("IPasswordHasher.Hash");
        hash.Returns("default-hash");
        hash.When("secret").Returns("secret-hash");

        hash.Invoke("secret").Should().Be("secret-hash");
        hash.Invoke("anything else").Should().Be("default-hash");
    }

    [Fact]
    public void UnmatchedCall_WithNoFallback_ReturnsDefault()
    {
        var hash = new MockMethod<string, string>();
        hash.When("secret").Returns("secret-hash");

        hash.Invoke("other").Should().BeNull();
    }

    [Fact]
    public void LaterRule_WinsOverEarlierOneForTheSameArgument()
    {
        var hash = new MockMethod<string, string>();
        hash.When("secret").Returns("first");
        hash.When("secret").Returns("second");

        hash.Invoke("secret").Should().Be("second");
    }

    [Fact]
    public void PredicateMatcher_Works()
    {
        var hash = new MockMethod<string, string>();
        hash.When(Arg.Is<string>(k => k.StartsWith("admin"))).Returns("privileged");
        hash.Returns("normal");

        hash.Invoke("admin:1").Should().Be("privileged");
        hash.Invoke("user:1").Should().Be("normal");
    }

    [Fact]
    public void OmittedMatcher_MatchesAnything()
    {
        var verify = new MockMethod<string, string, bool>();
        verify.When("correct-password").Returns(true);   // second argument unconstrained

        verify.Invoke("correct-password", "any-hash").Should().BeTrue();
        verify.Invoke("other", "any-hash").Should().BeFalse();
    }

    [Fact]
    public void Throws_AppliesOnlyToMatchingCalls()
    {
        var get = new MockMethod<string, string>();
        get.Returns("fine");
        get.When("explode").Throws(new InvalidOperationException("boom"));

        get.Invoke("ok").Should().Be("fine");
        get.Invoking(g => g.Invoke("explode")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Returns_WithFactory_SeesTheArguments()
    {
        var concat = new MockMethod<string, int, string>();
        concat.When(Arg.Any<string>(), Arg.Is<int>(n => n > 0)).Returns(call => $"{call.Item1}:{call.Item2}");

        concat.Invoke("a", 5).Should().Be("a:5");
    }

    [Fact]
    public void ConditionalCalls_AreStillRecordedForReceived()
    {
        var verify = new MockMethod<string, string, bool>("IPasswordHasher.Verify");
        verify.When("correct", "hash").Returns(true);

        verify.Invoke("correct", "hash");
        verify.Invoke("wrong", "hash");

        verify.Invoking(v => v.Received(2)).Should().NotThrow();
        verify.Invoking(v => v.Received(1, "correct")).Should().NotThrow();
    }

    // The regression this whole feature guards against: without When(), both configurations become
    // one, the second overwrites the first, and a test asserting the wrong password is rejected
    // passes because the mock now says false to everything.
    [Fact]
    public void WithoutWhen_TheSecondConfigurationOverwritesTheFirst()
    {
        var verify = new MockMethod<string, string, bool>();
        verify.Returns(true);
        verify.Returns(false);

        verify.Invoke("correct-password", "hash").Should().BeFalse();
    }
}
