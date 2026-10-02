// =============================================================================
// Try/TryAsync Factory Method Unit Tests
// =============================================================================

using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class TryCatchExtensionsTests
{
    // =========================================================================
    // Try<TValue, TError> (Sync with return value)
    // =========================================================================

    [Fact]
    public void TryCatch_WhenOperationSucceeds_ReturnsSuccess()
    {
        var result = Result.Try(
            () => 42,
            ex => new StringError(ex.Message));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void TryCatch_WhenOperationThrows_ReturnsFailure()
    {
        var result = Result.Try<int, StringError>(
            () => throw new InvalidOperationException("test error"),
            ex => new StringError(ex.Message));

        Assert.True(result.IsFailure);
        Assert.Equal("test error", result.Error.Message);
    }

    [Fact]
    public void TryCatch_WhenOperationThrows_PassesExceptionToMapper()
    {
        Exception? capturedEx = null;

        var result = Result.Try<int, StringError>(
            () => throw new InvalidOperationException("test error"),
            ex =>
            {
                capturedEx = ex;
                return new StringError(ex.Message);
            });

        Assert.NotNull(capturedEx);
        Assert.IsType<InvalidOperationException>(capturedEx);
    }

    [Fact]
    public void TryCatch_ThrowsOnNullOperation()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result.Try<int, StringError>(null!, ex => new StringError(ex.Message)));
    }

    [Fact]
    public void TryCatch_ThrowsOnNullErrorMapper()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result.Try(() => 42, (Func<Exception, StringError>)null!));
    }

    [Fact]
    public void TryCatch_CapturesInnerException()
    {
        var innerEx = new ArgumentException("inner");

        var result = Result.Try<int, StringError>(
            () => throw new InvalidOperationException("outer", innerEx),
            ex => new StringError(ex.InnerException?.Message ?? "no inner"));

        Assert.True(result.IsFailure);
        Assert.Equal("inner", result.Error.Message);
    }

    // =========================================================================
    // TryAsync<TValue, TError> (Async with return value)
    // =========================================================================

    [Fact]
    public async Task TryCatchAsync_WhenOperationSucceeds_ReturnsSuccess()
    {
        var result = await Result.TryAsync(
            async ct =>
            {
                await Task.Delay(1, ct);
                return 42;
            },
            ex => new StringError(ex.Message));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task TryCatchAsync_WhenOperationThrows_ReturnsFailure()
    {
        var result = await Result.TryAsync<int, StringError>(
            ct => throw new InvalidOperationException("async error"),
            ex => new StringError(ex.Message));

        Assert.True(result.IsFailure);
        Assert.Equal("async error", result.Error.Message);
    }

    [Fact]
    public async Task TryCatchAsync_WhenOperationThrowsAsync_ReturnsFailure()
    {
        var result = await Result.TryAsync<int, StringError>(
            async ct =>
            {
                await Task.Delay(1, ct);
                throw new InvalidOperationException("async error after delay");
            },
            ex => new StringError(ex.Message));

        Assert.True(result.IsFailure);
        Assert.Equal("async error after delay", result.Error.Message);
    }

    [Fact]
    public async Task TryCatchAsync_PassesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        CancellationToken receivedToken = default;

        var result = await Result.TryAsync(
            ct =>
            {
                receivedToken = ct;
                return Task.FromResult(42);
            },
            ex => new StringError(ex.Message),
            cts.Token);

        Assert.Equal(cts.Token, receivedToken);
    }

    [Fact]
    public async Task TryCatchAsync_WhenCancelled_CapturesOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Result.TryAsync<int, StringError>(
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                return await Task.FromResult(42);
            },
            ex => new StringError(ex.GetType().Name),
            cts.Token);

        Assert.True(result.IsFailure);
        Assert.Contains("Cancel", result.Error.Message);
    }

    [Fact]
    public async Task TryCatchAsync_ThrowsOnNullOperation()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Result.TryAsync<int, StringError>(null!, ex => new StringError(ex.Message)));
    }

    [Fact]
    public async Task TryCatchAsync_ThrowsOnNullErrorMapper()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Result.TryAsync(ct => Task.FromResult(42), (Func<Exception, StringError>)null!));
    }

    // =========================================================================
    // Try<TError> (Sync void operation)
    // =========================================================================

    [Fact]
    public void TryCatchVoid_WhenOperationSucceeds_ReturnsSuccess()
    {
        var executed = false;

        var result = Result.Try(
            () => executed = true,
            ex => new StringError(ex.Message));

        Assert.True(result.IsSuccess);
        Assert.True(executed);
    }

    [Fact]
    public void TryCatchVoid_WhenOperationThrows_ReturnsFailure()
    {
        var result = Result.Try<StringError>(
            () => throw new InvalidOperationException("void error"),
            ex => new StringError(ex.Message));

        Assert.True(result.IsFailure);
        Assert.Equal("void error", result.Error.Message);
    }

    [Fact]
    public void TryCatchVoid_ThrowsOnNullAction()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result.Try(null!, ex => new StringError(ex.Message)));
    }

    [Fact]
    public void TryCatchVoid_ThrowsOnNullErrorMapper()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Result.Try(() => { }, (Func<Exception, StringError>)null!));
    }

    // =========================================================================
    // TryAsync<TError> (Async void operation)
    // =========================================================================

    [Fact]
    public async Task TryCatchAsyncVoid_WhenOperationSucceeds_ReturnsSuccess()
    {
        var executed = false;

        var result = await Result.TryAsync(
            async ct =>
            {
                await Task.Delay(1, ct);
                executed = true;
            },
            ex => new StringError(ex.Message));

        Assert.True(result.IsSuccess);
        Assert.True(executed);
    }

    [Fact]
    public async Task TryCatchAsyncVoid_WhenOperationThrows_ReturnsFailure()
    {
        var result = await Result.TryAsync<StringError>(
            ct => throw new InvalidOperationException("async void error"),
            ex => new StringError(ex.Message));

        Assert.True(result.IsFailure);
        Assert.Equal("async void error", result.Error.Message);
    }

    [Fact]
    public async Task TryCatchAsyncVoid_PassesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        CancellationToken receivedToken = default;

        await Result.TryAsync(
            ct =>
            {
                receivedToken = ct;
                return Task.CompletedTask;
            },
            ex => new StringError(ex.Message),
            cts.Token);

        Assert.Equal(cts.Token, receivedToken);
    }

    [Fact]
    public async Task TryCatchAsyncVoid_ThrowsOnNullOperation()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Result.TryAsync<StringError>(null!, ex => new StringError(ex.Message)));
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void TryCatch_WithDifferentExceptionTypes_MapsCorrectly()
    {
        // ArgumentNullException
        var result1 = Result.Try<int, StringError>(
            () => throw new ArgumentNullException("param"),
            ex => new StringError(ex.GetType().Name));
        Assert.Equal("ArgumentNullException", result1.Error.Message);

        // FormatException
        var result2 = Result.Try<int, StringError>(
            () => throw new FormatException("bad format"),
            ex => new StringError(ex.GetType().Name));
        Assert.Equal("FormatException", result2.Error.Message);

        // Custom exception
        var result3 = Result.Try<int, StringError>(
            () => throw new CustomTestException("custom"),
            ex => new StringError(ex.GetType().Name));
        Assert.Equal("CustomTestException", result3.Error.Message);
    }

    [Fact]
    public void TryCatch_PreservesExceptionData()
    {
        var ex = new InvalidOperationException("error");
        ex.Data["key"] = "value";

        var result = Result.Try<int, StringError>(
            () => throw ex,
            caught => new StringError((string?)caught.Data["key"] ?? "no data"));

        Assert.Equal("value", result.Error.Message);
    }

    [Fact]
    public async Task TryCatchAsync_WithAggregateException_CapturesInner()
    {
        var result = await Result.TryAsync<int, StringError>(
            async ct =>
            {
                await Task.WhenAll(
                    Task.Run(() => throw new InvalidOperationException("task error"), ct)
                );
                return 42;
            },
            ex =>
            {
                var inner = ex is AggregateException agg
                    ? agg.InnerException?.Message ?? "no inner"
                    : ex.Message;
                return new StringError(inner);
            });

        Assert.True(result.IsFailure);
    }
}

// Helper exception for testing (not file-scoped so GetType().Name works correctly)
internal sealed class CustomTestException(string message) : Exception(message);
