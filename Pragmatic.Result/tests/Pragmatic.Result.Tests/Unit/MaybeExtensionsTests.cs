// =============================================================================
// MaybeExtensions Unit Tests
// =============================================================================

using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class MaybeExtensionsTests
{
    // =========================================================================
    // ToResult (eager error)
    // =========================================================================

    [Fact]
    public void ToResult_Some_ReturnsSuccess()
    {
        var maybe = Maybe<string>.Some("hello");

        var result = maybe.ToResult(new StringError("not found"));

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void ToResult_None_ReturnsFailure()
    {
        var maybe = Maybe<string>.None();

        var result = maybe.ToResult(new StringError("not found"));

        Assert.True(result.IsFailure);
        Assert.Equal("not found", result.Error.Message);
    }

    [Fact]
    public void ToResult_SomeInt_ReturnsSuccess()
    {
        var maybe = Maybe<int>.Some(42);

        var result = maybe.ToResult(new StringError("missing"));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    // =========================================================================
    // ToResult (lazy error)
    // =========================================================================

    [Fact]
    public void ToResult_Lazy_Some_DoesNotCallFactory()
    {
        var maybe = Maybe<string>.Some("value");
        var factoryCalled = false;

        var result = maybe.ToResult(() =>
        {
            factoryCalled = true;
            return new StringError("error");
        });

        Assert.True(result.IsSuccess);
        Assert.False(factoryCalled);
    }

    [Fact]
    public void ToResult_Lazy_None_CallsFactory()
    {
        var maybe = Maybe<string>.None();

        var result = maybe.ToResult(() => new StringError("lazy error"));

        Assert.True(result.IsFailure);
        Assert.Equal("lazy error", result.Error.Message);
    }

    // =========================================================================
    // ToMaybe
    // =========================================================================

    [Fact]
    public void ToMaybe_Success_ReturnsSome()
    {
        var result = Result<string, StringError>.Success("hello");

        var maybe = result.ToMaybe();

        Assert.True(maybe.HasValue);
        Assert.Equal("hello", maybe.Value);
    }

    [Fact]
    public void ToMaybe_Failure_ReturnsNone()
    {
        var result = Result<string, StringError>.Failure(new StringError("oops"));

        var maybe = result.ToMaybe();

        Assert.True(maybe.IsNone);
    }

    // =========================================================================
    // Roundtrip
    // =========================================================================

    [Fact]
    public void Roundtrip_Some_ToResult_ToMaybe_PreservesValue()
    {
        var original = Maybe<int>.Some(99);

        var roundtripped = original
            .ToResult(new StringError("err"))
            .ToMaybe();

        Assert.True(roundtripped.HasValue);
        Assert.Equal(99, roundtripped.Value);
    }

    [Fact]
    public void Roundtrip_None_ToResult_ToMaybe_StaysNone()
    {
        var original = Maybe<int>.None();

        var roundtripped = original
            .ToResult(new StringError("err"))
            .ToMaybe();

        Assert.True(roundtripped.IsNone);
    }
}
