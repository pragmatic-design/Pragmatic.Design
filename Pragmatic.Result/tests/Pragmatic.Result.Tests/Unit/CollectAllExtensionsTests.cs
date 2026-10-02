// =============================================================================
// CollectAll Extensions Unit Tests
// =============================================================================

using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class CollectAllExtensionsTests
{
    // =========================================================================
    // CollectAll (IEnumerable) Tests
    // =========================================================================

    [Fact]
    public void CollectAll_AllSuccess_ReturnsAllValues()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Success(2),
            Result<int, StringError>.Success(3)
        };

        var collected = ResultExtensions.CollectAll(results.AsEnumerable());

        Assert.True(collected.IsSuccess);
        Assert.Equal(3, collected.Value.Count);
        Assert.Equal([1, 2, 3], collected.Value);
    }

    [Fact]
    public void CollectAll_AllFailure_ReturnsAggregateError()
    {
        var results = new[]
        {
            Result<int, StringError>.Failure(new StringError("error1")),
            Result<int, StringError>.Failure(new StringError("error2")),
            Result<int, StringError>.Failure(new StringError("error3"))
        };

        var collected = ResultExtensions.CollectAll(results.AsEnumerable());

        Assert.True(collected.IsFailure);
        Assert.Equal(3, collected.Error.Count);
        Assert.Equal("error1", ((StringError)collected.Error.Errors[0]).Message);
        Assert.Equal("error2", ((StringError)collected.Error.Errors[1]).Message);
        Assert.Equal("error3", ((StringError)collected.Error.Errors[2]).Message);
    }

    [Fact]
    public void CollectAll_MixedResults_CollectsAllErrors()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Failure(new StringError("error1")),
            Result<int, StringError>.Success(3),
            Result<int, StringError>.Failure(new StringError("error2"))
        };

        var collected = ResultExtensions.CollectAll(results.AsEnumerable());

        Assert.True(collected.IsFailure);
        Assert.Equal(2, collected.Error.Count);
    }

    [Fact]
    public void CollectAll_SingleSuccess_ReturnsSuccess()
    {
        var results = new[] { Result<int, StringError>.Success(42) };

        var collected = ResultExtensions.CollectAll(results.AsEnumerable());

        Assert.True(collected.IsSuccess);
        Assert.Single(collected.Value);
        Assert.Equal(42, collected.Value[0]);
    }

    [Fact]
    public void CollectAll_SingleFailure_ReturnsAggregateWithOneError()
    {
        var results = new[] { Result<int, StringError>.Failure(new StringError("single error")) };

        var collected = ResultExtensions.CollectAll(results.AsEnumerable());

        Assert.True(collected.IsFailure);
        Assert.Single(collected.Error.Errors);
    }

    [Fact]
    public void CollectAll_EmptyEnumerable_ReturnsEmptySuccess()
    {
        var results = Enumerable.Empty<Result<int, StringError>>();

        var collected = ResultExtensions.CollectAll(results);

        Assert.True(collected.IsSuccess);
        Assert.Empty(collected.Value);
    }

    [Fact]
    public void CollectAll_ThrowsOnNullEnumerable()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ResultExtensions.CollectAll((IEnumerable<Result<int, StringError>>)null!));
    }

    // =========================================================================
    // CollectAll (Params/Span) Tests
    // =========================================================================

    [Fact]
    public void CollectAll_Params_AllSuccess_ReturnsAllValues()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<int, StringError>.Success(2);
        var r3 = Result<int, StringError>.Success(3);

        var collected = ResultExtensions.CollectAll(r1, r2, r3);

        Assert.True(collected.IsSuccess);
        Assert.Equal([1, 2, 3], collected.Value);
    }

    [Fact]
    public void CollectAll_Params_AllFailure_ReturnsAggregateError()
    {
        var r1 = Result<int, StringError>.Failure(new StringError("e1"));
        var r2 = Result<int, StringError>.Failure(new StringError("e2"));

        var collected = ResultExtensions.CollectAll(r1, r2);

        Assert.True(collected.IsFailure);
        Assert.Equal(2, collected.Error.Count);
    }

    [Fact]
    public void CollectAll_Params_MixedResults_CollectsAllErrors()
    {
        var r1 = Result<int, StringError>.Success(1);
        var r2 = Result<int, StringError>.Failure(new StringError("e1"));
        var r3 = Result<int, StringError>.Success(3);
        var r4 = Result<int, StringError>.Failure(new StringError("e2"));

        var collected = ResultExtensions.CollectAll(r1, r2, r3, r4);

        Assert.True(collected.IsFailure);
        Assert.Equal(2, collected.Error.Count);
    }

    // =========================================================================
    // CollectAll vs Combine Behavior Difference
    // =========================================================================

    [Fact]
    public void CollectAll_CollectsAllErrors_UnlikeCombine()
    {
        // Combine fails fast (returns first error)
        var r1 = Result<int, StringError>.Failure(new StringError("first"));
        var r2 = Result<int, StringError>.Failure(new StringError("second"));

        var combined = ResultExtensions.Combine(r1, r2);
        Assert.Equal("first", combined.Error.Message); // Only first error

        // CollectAll collects all errors
        var collected = ResultExtensions.CollectAll(r1, r2);
        Assert.Equal(2, collected.Error.Count); // All errors
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void CollectAll_WithDifferentErrorTypes_Works()
    {
        // All errors inherit from Error base class
        var results = new[]
        {
            Result<int, Error>.Failure(new StringError("string")),
            Result<int, Error>.Failure(new TestNotFoundError("resource")),
            Result<int, Error>.Failure(new TestValidationError("validation"))
        };

        var collected = ResultExtensions.CollectAll(results.AsEnumerable());

        Assert.True(collected.IsFailure);
        Assert.Equal(3, collected.Error.Count);
        Assert.IsType<StringError>(collected.Error.Errors[0]);
        Assert.IsType<TestNotFoundError>(collected.Error.Errors[1]);
        Assert.IsType<TestValidationError>(collected.Error.Errors[2]);
    }

    [Fact]
    public void CollectAll_PreservesOrder()
    {
        var results = Enumerable.Range(1, 10)
            .Select(i => Result<int, StringError>.Success(i));

        var collected = ResultExtensions.CollectAll(results);

        Assert.True(collected.IsSuccess);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], collected.Value);
    }

    [Fact]
    public void CollectAll_LargeCollection_Works()
    {
        var results = Enumerable.Range(1, 1000)
            .Select(i => Result<int, StringError>.Success(i));

        var collected = ResultExtensions.CollectAll(results);

        Assert.True(collected.IsSuccess);
        Assert.Equal(1000, collected.Value.Count);
    }

    [Fact]
    public void CollectAll_LargeErrorCollection_Works()
    {
        var results = Enumerable.Range(1, 100)
            .Select(i => Result<int, StringError>.Failure(new StringError($"error{i}")));

        var collected = ResultExtensions.CollectAll(results);

        Assert.True(collected.IsFailure);
        Assert.Equal(100, collected.Error.Count);
    }

    [Fact]
    public void CollectAll_WithReferenceTypes_Works()
    {
        // Result<T>.Success throws on null, so use valid strings
        var results = new[]
        {
            Result<string, StringError>.Success("first"),
            Result<string, StringError>.Success("second"),
            Result<string, StringError>.Success("third")
        };

        var collected = ResultExtensions.CollectAll(results.AsEnumerable());

        Assert.True(collected.IsSuccess);
        Assert.Equal(["first", "second", "third"], collected.Value);
    }

    [Fact]
    public void CollectAll_AggregateError_HasCorrectStatusCode()
    {
        // AggregateError returns highest status code
        var results = new[]
        {
            Result<int, Error>.Failure(new StringError("400")), // 400
            Result<int, Error>.Failure(new TestNotFoundError("item")), // 404
            Result<int, Error>.Failure(new TestUnauthorizedError()) // 401
        };

        var collected = ResultExtensions.CollectAll(results.AsEnumerable());

        Assert.True(collected.IsFailure);
        Assert.Equal(404, collected.Error.StatusCode); // Highest is 404
    }

    // =========================================================================
    // Extension-method call style (#31)
    // =========================================================================

    [Fact]
    public void CollectAll_CalledAsExtensionOnEnumerable_Works()
    {
        IEnumerable<Result<int, StringError>> results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Success(2)
        };

        var collected = results.CollectAll();

        Assert.True(collected.IsSuccess);
        Assert.Equal([1, 2], collected.Value);
    }

    [Fact]
    public void CollectAll_CalledAsExtensionOnArray_CollectsErrors()
    {
        var results = new[]
        {
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Failure(new StringError("bad"))
        };

        var collected = results.CollectAll();

        Assert.True(collected.IsFailure);
        Assert.Equal(1, collected.Error.Count);
    }
}