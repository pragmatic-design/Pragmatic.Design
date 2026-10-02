// =============================================================================
// Maybe<TValue> Unit Tests
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class MaybeTests
{
    // =========================================================================
    // Factory Methods
    // =========================================================================

    [Fact]
    public void Some_CreatesWithValue()
    {
        var maybe = Maybe<int>.Some(42);

        Assert.True(maybe.HasValue);
        Assert.Equal(42, maybe.Value);
    }

    [Fact]
    public void Some_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Maybe<string>.Some(null!));
    }

    [Fact]
    public void None_CreatesEmpty()
    {
        var maybe = Maybe<int>.None();

        Assert.False(maybe.HasValue);
        Assert.True(maybe.IsNone);
    }

    // =========================================================================
    // Property Access
    // =========================================================================

    [Fact]
    public void Value_ThrowsWhenNone()
    {
        var maybe = Maybe<int>.None();

        Assert.Throws<InvalidOperationException>(() => _ = maybe.Value);
    }

    // =========================================================================
    // TryGetValue
    // =========================================================================

    [Fact]
    public void TryGetValue_ReturnsTrueWhenSome()
    {
        var maybe = Maybe<int>.Some(42);

        var success = maybe.TryGetValue(out var value);

        Assert.True(success);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGetValue_ReturnsFalseWhenNone()
    {
        var maybe = Maybe<int>.None();

        var success = maybe.TryGetValue(out var value);

        Assert.False(success);
        Assert.Equal(default, value);
    }

    // =========================================================================
    // Match Methods
    // =========================================================================

    [Fact]
    public void Match_CallsOnSomeWhenHasValue()
    {
        var maybe = Maybe<int>.Some(42);

        var output = maybe.Match(
            v => $"value: {v}",
            () => "none");

        Assert.Equal("value: 42", output);
    }

    [Fact]
    public void Match_CallsOnNoneWhenEmpty()
    {
        var maybe = Maybe<int>.None();

        var output = maybe.Match(
            v => $"value: {v}",
            () => "none");

        Assert.Equal("none", output);
    }

    [Fact]
    public void MatchAction_CallsOnSomeWhenHasValue()
    {
        var maybe = Maybe<int>.Some(42);
        var called = false;

        maybe.Match(
            v => called = true,
            () => called = false);

        Assert.True(called);
    }

    [Fact]
    public void MatchAction_CallsOnNoneWhenEmpty()
    {
        var maybe = Maybe<int>.None();
        var called = false;

        maybe.Match(
            v => called = false,
            () => called = true);

        Assert.True(called);
    }

    // =========================================================================
    // Map Method
    // =========================================================================

    [Fact]
    public void Map_TransformsValueWhenSome()
    {
        var maybe = Maybe<int>.Some(42);

        var mapped = maybe.Map(v => v * 2);

        Assert.True(mapped.HasValue);
        Assert.Equal(84, mapped.Value);
    }

    [Fact]
    public void Map_ReturnsNoneWhenNone()
    {
        var maybe = Maybe<int>.None();

        var mapped = maybe.Map(v => v * 2);

        Assert.False(mapped.HasValue);
    }

    // =========================================================================
    // Bind Method
    // =========================================================================

    [Fact]
    public void Bind_ChainsWhenSome()
    {
        var maybe = Maybe<int>.Some(42);

        var bound = maybe.Bind(v =>
            Maybe<string>.Some($"value: {v}"));

        Assert.True(bound.HasValue);
        Assert.Equal("value: 42", bound.Value);
    }

    [Fact]
    public void Bind_ReturnsNoneWhenNone()
    {
        var maybe = Maybe<int>.None();

        var bound = maybe.Bind(v =>
            Maybe<string>.Some($"value: {v}"));

        Assert.False(bound.HasValue);
    }

    [Fact]
    public void Bind_PropagatesNone()
    {
        var maybe = Maybe<int>.Some(42);

        var bound = maybe.Bind(v => Maybe<string>.None());

        Assert.False(bound.HasValue);
    }

    // =========================================================================
    // GetValueOrDefault
    // =========================================================================

    [Fact]
    public void GetValueOrDefault_ReturnsValueWhenSome()
    {
        var maybe = Maybe<int>.Some(42);

        var value = maybe.GetValueOrDefault(0);

        Assert.Equal(42, value);
    }

    [Fact]
    public void GetValueOrDefault_ReturnsDefaultWhenNone()
    {
        var maybe = Maybe<int>.None();

        var value = maybe.GetValueOrDefault(0);

        Assert.Equal(0, value);
    }

    [Fact]
    public void GetValueOrDefault_WithFactory_ReturnsValueWhenSome()
    {
        var maybe = Maybe<int>.Some(42);
        var factoryCalled = false;

        var value = maybe.GetValueOrDefault(() =>
        {
            factoryCalled = true;
            return 0;
        });

        Assert.Equal(42, value);
        Assert.False(factoryCalled);
    }

    [Fact]
    public void GetValueOrDefault_WithFactory_CallsFactoryWhenNone()
    {
        var maybe = Maybe<int>.None();
        var factoryCalled = false;

        var value = maybe.GetValueOrDefault(() =>
        {
            factoryCalled = true;
            return 99;
        });

        Assert.Equal(99, value);
        Assert.True(factoryCalled);
    }

    // =========================================================================
    // Implicit Operators
    // =========================================================================

    [Fact]
    public void ImplicitOperator_FromValue()
    {
        Maybe<int> maybe = 42;

        Assert.True(maybe.HasValue);
        Assert.Equal(42, maybe.Value);
    }

    [Fact]
    public void ImplicitOperator_ToBool()
    {
        Maybe<int> some = 42;
        var none = Maybe<int>.None();

        Assert.True(some);
        Assert.False(none);
    }
}