// =============================================================================
// ResultAsyncEnumerableExtensions Unit Tests
// =============================================================================

using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class AsyncEnumerableExtensionsTests
{
    // =========================================================================
    // CollectAsync
    // =========================================================================

    [Fact]
    public async Task CollectAsync_AllSuccess_ReturnsSuccessWithAllValues()
    {
        var source = ToAsyncEnumerable(
            Result<int, IError>.Success(1),
            Result<int, IError>.Success(2),
            Result<int, IError>.Success(3));

        var result = await source.CollectAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal([1, 2, 3], result.Value);
    }

    [Fact]
    public async Task CollectAsync_SomeFailures_ReturnsAggregateError()
    {
        var source = ToAsyncEnumerable(
            Result<int, IError>.Success(1),
            Result<int, IError>.Failure(new StringError("err1")),
            Result<int, IError>.Success(3),
            Result<int, IError>.Failure(new TestValidationError("err2")));

        var result = await source.CollectAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Error.Count);
    }

    [Fact]
    public async Task CollectAsync_Empty_ReturnsEmptyList()
    {
        var source = ToAsyncEnumerable<Result<int, IError>>();

        var result = await source.CollectAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task CollectAsync_AllFailures_ReturnsAllErrors()
    {
        var source = ToAsyncEnumerable(
            Result<int, IError>.Failure(new StringError("a")),
            Result<int, IError>.Failure(new StringError("b")));

        var result = await source.CollectAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(2, result.Error.Count);
    }

    // =========================================================================
    // FilterSuccessesAsync
    // =========================================================================

    [Fact]
    public async Task FilterSuccessesAsync_MixedResults_YieldsOnlySuccesses()
    {
        var source = ToAsyncEnumerable(
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Failure(new StringError("err")),
            Result<int, StringError>.Success(3));

        var successes = new List<int>();
        await foreach (var value in source.FilterSuccessesAsync())
            successes.Add(value);

        Assert.Equal([1, 3], successes);
    }

    [Fact]
    public async Task FilterSuccessesAsync_AllFailures_YieldsNothing()
    {
        var source = ToAsyncEnumerable(
            Result<int, StringError>.Failure(new StringError("a")),
            Result<int, StringError>.Failure(new StringError("b")));

        var successes = new List<int>();
        await foreach (var value in source.FilterSuccessesAsync())
            successes.Add(value);

        Assert.Empty(successes);
    }

    // =========================================================================
    // FilterFailuresAsync
    // =========================================================================

    [Fact]
    public async Task FilterFailuresAsync_MixedResults_YieldsOnlyErrors()
    {
        var source = ToAsyncEnumerable(
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Failure(new StringError("err")),
            Result<int, StringError>.Success(3));

        var errors = new List<StringError>();
        await foreach (var error in source.FilterFailuresAsync())
            errors.Add(error);

        Assert.Single(errors);
        Assert.Equal("err", errors[0].Message);
    }

    // =========================================================================
    // PartitionAsync
    // =========================================================================

    [Fact]
    public async Task PartitionAsync_MixedResults_SplitsCorrectly()
    {
        var source = ToAsyncEnumerable(
            Result<int, StringError>.Success(1),
            Result<int, StringError>.Failure(new StringError("a")),
            Result<int, StringError>.Success(2),
            Result<int, StringError>.Failure(new StringError("b")));

        var (successes, failures) = await source.PartitionAsync();

        Assert.Equal([1, 2], successes);
        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public async Task PartitionAsync_Empty_ReturnsBothEmpty()
    {
        var source = ToAsyncEnumerable<Result<int, StringError>>();

        var (successes, failures) = await source.PartitionAsync();

        Assert.Empty(successes);
        Assert.Empty(failures);
    }

    // =========================================================================
    // Cancellation
    // =========================================================================

    [Fact]
    public async Task CollectAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var items = new[]
        {
            Result<int, IError>.Success(1),
            Result<int, IError>.Success(2)
        };

        var source = ToAsyncEnumerableWithCancellation(items);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => source.CollectAsync(cts.Token));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerableWithCancellation<T>(
        T[] items,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }
}
