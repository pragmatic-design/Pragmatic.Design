using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Xunit;

namespace Pragmatic.Testing.Tests.Mocking;

public sealed class MockPropertyTests
{
    [Fact]
    public void Get_NotConfigured_ReturnsDefaultAndStillCounts()
    {
        var property = new MockProperty<int>("IThing.Value");

        property.Get().Should().Be(0);
        property.ReadCount.Should().Be(1);
    }

    [Fact]
    public void Returns_Value_IsReturnedOnEveryRead()
    {
        var property = new MockProperty<string>().Returns("v");

        property.Get().Should().Be("v");
        property.Get().Should().Be("v");
        property.ReadCount.Should().Be(2);
    }

    [Fact]
    public void Returns_Factory_IsEvaluatedPerRead()
    {
        var n = 0;
        var property = new MockProperty<int>().Returns(() => ++n);

        property.Get().Should().Be(1);
        property.Get().Should().Be(2);
    }

    [Fact]
    public void Throws_Configured_GetThrowsAndTheReadIsStillCounted()
    {
        var boom = new InvalidOperationException("boom");
        var property = new MockProperty<int>().Throws(boom);

        property.Invoking(p => p.Get()).Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(boom);
        property.ReadCount.Should().Be(1);
    }

    [Fact]
    public void Set_RecordsTheValueAndBecomesWhatSubsequentReadsReturn()
    {
        var property = new MockProperty<string>().Returns("initial");

        property.Set("assigned");

        property.AssignedValues.Should().Equal("assigned");
        property.Get().Should().Be("assigned");
    }

    [Fact]
    public void Received_MatchingCount_DoesNotThrow()
    {
        var property = new MockProperty<int>();
        property.Get();
        property.Get();

        property.Invoking(p => p.Received(2)).Should().NotThrow();
    }

    // The assertion has to FAIL when the expectation is not met — the whole point of owning this.
    [Fact]
    public void Received_WrongCount_Throws()
    {
        var property = new MockProperty<int>("IClock.UtcNow");
        property.Get();

        property.Invoking(p => p.Received(2))
            .Should().Throw<PragmaticTestAssertionException>()
            .WithMessage("*IClock.UtcNow*2 times*1 time*");
    }

    [Fact]
    public void DidNotReceive_WhenRead_Throws()
    {
        var property = new MockProperty<int>("IClock.UtcNow");
        property.Get();

        property.Invoking(p => p.DidNotReceive())
            .Should().Throw<PragmaticTestAssertionException>();
    }

    [Fact]
    public void ReceivedSet_WithMatcher_CountsOnlyMatchingAssignments()
    {
        var property = new MockProperty<string>("IThing.Name");
        property.Set("a");
        property.Set("b");
        property.Set("a");

        property.Invoking(p => p.ReceivedSet(2, "a")).Should().NotThrow();
        property.Invoking(p => p.ReceivedSet(1, "b")).Should().NotThrow();
        property.Invoking(p => p.ReceivedSet(3, "a")).Should().Throw<PragmaticTestAssertionException>();
    }
}
