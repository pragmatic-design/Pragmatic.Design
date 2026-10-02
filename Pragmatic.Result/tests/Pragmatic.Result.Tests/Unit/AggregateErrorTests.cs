// =============================================================================
// AggregateError Unit Tests
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class AggregateErrorTests
{
    // =========================================================================
    // Constructor Tests
    // =========================================================================

    [Fact]
    public void Constructor_WithParamsErrors_CreatesAggregateError()
    {
        var error1 = new StringError("error1");
        var error2 = new TestValidationError("error2");

        var aggregate = new AggregateError(error1, error2);

        Assert.Equal(2, aggregate.Count);
        Assert.Equal(error1, aggregate.Errors[0]);
        Assert.Equal(error2, aggregate.Errors[1]);
    }

    [Fact]
    public void Constructor_WithEnumerable_CreatesAggregateError()
    {
        var errors = new Error[] { new StringError("e1"), new StringError("e2") };

        var aggregate = new AggregateError(errors.AsEnumerable());

        Assert.Equal(2, aggregate.Count);
    }

    [Fact]
    public void Constructor_WithNullParams_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AggregateError(null!));
    }

    [Fact]
    public void Constructor_WithNullEnumerable_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AggregateError((IEnumerable<Error>)null!));
    }

    [Fact]
    public void Constructor_WithEmptyArray_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new AggregateError());
    }

    [Fact]
    public void Constructor_WithSingleError_CreatesAggregateError()
    {
        var error = new StringError("single");

        var aggregate = new AggregateError(error);

        Assert.Equal(1, aggregate.Count);
        Assert.Equal(error, aggregate.Errors[0]);
    }

    // =========================================================================
    // Property Tests
    // =========================================================================

    [Fact]
    public void Code_ReturnsAggregateError()
    {
        var aggregate = new AggregateError(new StringError("e"));

        Assert.Equal("AGGREGATE_ERROR", aggregate.Code);
    }

    [Fact]
    public void StatusCode_ReturnsHighestStatusCode()
    {
        var error400 = new StringError("400"); // StatusCode = 400
        var error404 = new TestNotFoundError("resource"); // StatusCode = 404
        var error401 = new TestUnauthorizedError(); // StatusCode = 401

        var aggregate = new AggregateError(error400, error404, error401);

        Assert.Equal(404, aggregate.StatusCode);
    }

    [Fact]
    public void StatusCode_WithMixedSeverity_ReturnsHighest()
    {
        var error400 = new StringError("400");
        var error500 = new ServerError();

        var aggregate = new AggregateError(error400, error500);

        Assert.Equal(500, aggregate.StatusCode);
    }

    [Fact]
    public void Title_ReturnsCountOfErrors()
    {
        var aggregate = new AggregateError(
            new StringError("1"),
            new StringError("2"),
            new StringError("3")
        );

        Assert.Equal("Multiple Errors (3)", aggregate.Title);
    }

    [Fact]
    public void IsTransient_WhenAllTransient_ReturnsTrue()
    {
        var transient1 = new TransientError();
        var transient2 = new TransientError();

        var aggregate = new AggregateError(transient1, transient2);

        Assert.True(aggregate.IsTransient);
    }

    [Fact]
    public void IsTransient_WhenAnyNonTransient_ReturnsFalse()
    {
        var transient = new TransientError();
        var nonTransient = new StringError("non-transient");

        var aggregate = new AggregateError(transient, nonTransient);

        Assert.False(aggregate.IsTransient);
    }

    [Fact]
    public void RetryAfter_ReturnsMaxRetryAfter()
    {
        var error1 = new RetryableError(TimeSpan.FromSeconds(10));
        var error2 = new RetryableError(TimeSpan.FromSeconds(30));
        var error3 = new RetryableError(TimeSpan.FromSeconds(5));

        var aggregate = new AggregateError(error1, error2, error3);

        Assert.Equal(TimeSpan.FromSeconds(30), aggregate.RetryAfter);
    }

    [Fact]
    public void RetryAfter_WhenNoRetryAfter_ReturnsNull()
    {
        // When no errors have RetryAfter, returns null (no retry needed)
        var aggregate = new AggregateError(new StringError("e1"), new StringError("e2"));

        Assert.Null(aggregate.RetryAfter);
    }

    // =========================================================================
    // Factory Methods
    // =========================================================================

    [Fact]
    public void FromErrors_CreatesAggregateError()
    {
        var errors = new[] { new StringError("1"), new StringError("2") };

        var aggregate = AggregateError.FromErrors(errors);

        Assert.Equal(2, aggregate.Count);
    }

    [Fact]
    public void FromFailures_ExtractsErrorsFromFailedResults()
    {
        var success = Result<int, IError>.Success(1);
        var failure1 = Result<int, IError>.Failure(new StringError("fail1"));
        var failure2 = Result<int, IError>.Failure(new StringError("fail2"));

        var aggregate = AggregateError.FromFailures(success, failure1, failure2);

        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate!.Count);
    }

    [Fact]
    public void FromFailures_WithAllSuccess_ReturnsNull()
    {
        var success1 = Result<int, IError>.Success(1);
        var success2 = Result<int, IError>.Success(2);

        // Aligned with From/FromMany: no failures => null (not an exception).
        Assert.Null(AggregateError.FromFailures(success1, success2));
    }

    [Fact]
    public void FromFailures_WithConcreteErrorType_IsPassableWithoutRetyping()
    {
        // #32: concrete error type binds via inference, no Result<T, IError> re-typing needed.
        var found = Result<int, TestNotFoundError>.Success(1);
        var missing = Result<int, TestNotFoundError>.Failure(new TestNotFoundError("User"));

        var aggregate = AggregateError.FromFailures(found, missing);

        Assert.NotNull(aggregate);
        Assert.Equal(1, aggregate!.Count);
    }

    [Fact]
    public void From_TwoResults_WhenBothFail_ReturnsAggregateError()
    {
        var r1 = Result<int, IError>.Failure(new StringError("e1"));
        var r2 = Result<string, IError>.Failure(new StringError("e2"));

        var aggregate = AggregateError.From(r1, r2);

        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate!.Count);
    }

    [Fact]
    public void From_TwoResults_WhenOneFails_ReturnsAggregateError()
    {
        var r1 = Result<int, IError>.Success(1);
        var r2 = Result<string, IError>.Failure(new StringError("e2"));

        var aggregate = AggregateError.From(r1, r2);

        Assert.NotNull(aggregate);
        Assert.Equal(1, aggregate!.Count);
    }

    [Fact]
    public void From_TwoResults_WhenAllSuccess_ReturnsNull()
    {
        var r1 = Result<int, IError>.Success(1);
        var r2 = Result<string, IError>.Success("ok");

        var aggregate = AggregateError.From(r1, r2);

        Assert.Null(aggregate);
    }

    [Fact]
    public void From_ThreeResults_WhenAnyFails_ReturnsAggregateError()
    {
        var r1 = Result<int, IError>.Failure(new StringError("e1"));
        var r2 = Result<string, IError>.Success("ok");
        var r3 = Result<bool, IError>.Failure(new StringError("e3"));

        var aggregate = AggregateError.From(r1, r2, r3);

        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate!.Count);
    }

    [Fact]
    public void From_ThreeResults_WhenAllSuccess_ReturnsNull()
    {
        var r1 = Result<int, IError>.Success(1);
        var r2 = Result<string, IError>.Success("ok");
        var r3 = Result<bool, IError>.Success(true);

        var aggregate = AggregateError.From(r1, r2, r3);

        Assert.Null(aggregate);
    }

    // =========================================================================
    // From — 4 and 5 results
    // =========================================================================

    [Fact]
    public void From_FourResults_WhenAnyFails_ReturnsAggregateError()
    {
        var r1 = Result<int, IError>.Failure(new StringError("e1"));
        var r2 = Result<string, IError>.Success("ok");
        var r3 = Result<bool, IError>.Success(true);
        var r4 = Result<double, IError>.Failure(new StringError("e4"));

        var aggregate = AggregateError.From(r1, r2, r3, r4);

        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate!.Count);
    }

    [Fact]
    public void From_FourResults_WhenAllSuccess_ReturnsNull()
    {
        var r1 = Result<int, IError>.Success(1);
        var r2 = Result<string, IError>.Success("ok");
        var r3 = Result<bool, IError>.Success(true);
        var r4 = Result<double, IError>.Success(3.14);

        Assert.Null(AggregateError.From(r1, r2, r3, r4));
    }

    [Fact]
    public void From_FiveResults_WhenAnyFails_ReturnsAggregateError()
    {
        var r1 = Result<int, IError>.Success(1);
        var r2 = Result<string, IError>.Failure(new StringError("e2"));
        var r3 = Result<bool, IError>.Success(true);
        var r4 = Result<double, IError>.Success(3.14);
        var r5 = Result<long, IError>.Failure(new StringError("e5"));

        var aggregate = AggregateError.From(r1, r2, r3, r4, r5);

        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate!.Count);
    }

    [Fact]
    public void From_FiveResults_WhenAllSuccess_ReturnsNull()
    {
        var r1 = Result<int, IError>.Success(1);
        var r2 = Result<string, IError>.Success("ok");
        var r3 = Result<bool, IError>.Success(true);
        var r4 = Result<double, IError>.Success(3.14);
        var r5 = Result<long, IError>.Success(42L);

        Assert.Null(AggregateError.From(r1, r2, r3, r4, r5));
    }

    // =========================================================================
    // FromMany
    // =========================================================================

    [Fact]
    public void FromMany_MixedResults_ReturnsOnlyFailures()
    {
        var results = new[]
        {
            Result<int, IError>.Success(1),
            Result<int, IError>.Failure(new StringError("a")),
            Result<int, IError>.Success(2),
            Result<int, IError>.Failure(new StringError("b"))
        };

        var aggregate = AggregateError.FromMany(results);

        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate!.Count);
    }

    [Fact]
    public void FromMany_AllSuccess_ReturnsNull()
    {
        var results = new[]
        {
            Result<int, IError>.Success(1),
            Result<int, IError>.Success(2),
            Result<int, IError>.Success(3)
        };

        Assert.Null(AggregateError.FromMany(results));
    }

    [Fact]
    public void FromMany_Empty_ReturnsNull()
    {
        Assert.Null(AggregateError.FromMany<int, IError>());
    }

    [Fact]
    public void From_WithConcreteErrorTypes_IsPassableWithoutRetyping()
    {
        // #32: heterogeneous concrete error types bind via inference.
        var r1 = Result<int, TestNotFoundError>.Failure(new TestNotFoundError("User"));
        var r2 = Result<string, StringError>.Failure(new StringError("bad"));

        var aggregate = AggregateError.From(r1, r2);

        Assert.NotNull(aggregate);
        Assert.Equal(2, aggregate!.Count);
    }

    [Fact]
    public void FromMany_SingleFailure_ReturnsSingleError()
    {
        var aggregate = AggregateError.FromMany(
            Result<int, IError>.Failure(new StringError("only")));

        Assert.NotNull(aggregate);
        Assert.Equal(1, aggregate!.Count);
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void Errors_IsImmutable()
    {
        var aggregate = new AggregateError(new StringError("e"));

        var errors = aggregate.Errors;

        // Should be IReadOnlyList, not List
        Assert.IsAssignableFrom<IReadOnlyList<IError>>(errors);
    }

    [Fact]
    public void StatusCode_WithSingleError_ReturnsThatStatusCode()
    {
        var aggregate = new AggregateError(new TestNotFoundError("r"));

        Assert.Equal(404, aggregate.StatusCode);
    }
}

// Helper test error types
file sealed record ServerError : Error
{
    public override string Code => "SERVER_ERROR";
    public override int StatusCode => 500;
}

file sealed record TransientError : Error
{
    public override string Code => "TRANSIENT";
    public override int StatusCode => 503;
    public override bool IsTransient => true;
}

file sealed record RetryableError(TimeSpan Delay) : Error
{
    public override string Code => "RETRYABLE";
    public override int StatusCode => 503;
    public override TimeSpan? RetryAfter => Delay;
}