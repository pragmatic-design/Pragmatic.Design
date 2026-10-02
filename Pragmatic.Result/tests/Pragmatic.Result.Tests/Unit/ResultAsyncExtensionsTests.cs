// =============================================================================
// ResultAsyncExtensions Unit Tests
// =============================================================================

using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class ResultAsyncExtensionsTests
{
    // =========================================================================
    // MapAsync Tests
    // =========================================================================

    [Fact]
    public async Task MapAsync_Task_TransformsValueWhenSuccess()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));

        var mapped = await resultTask.MapAsync(v => v * 2);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(84, mapped.Value);
    }

    [Fact]
    public async Task MapAsync_Task_PreservesErrorWhenFailure()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Failure(new StringError("error")));

        var mapped = await resultTask.MapAsync(v => v * 2);

        Assert.True(mapped.IsFailure);
        Assert.Equal("error", mapped.Error.Message);
    }

    [Fact]
    public async Task MapAsync_AsyncMapper_TransformsValueWhenSuccess()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));

        var mapped = await resultTask.MapAsync(async v =>
        {
            await Task.Delay(1);
            return v * 2;
        });

        Assert.True(mapped.IsSuccess);
        Assert.Equal(84, mapped.Value);
    }

    [Fact]
    public async Task MapAsync_Result_AsyncMapper_TransformsValueWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);

        var mapped = await result.MapAsync(async v =>
        {
            await Task.Delay(1);
            return v * 2;
        });

        Assert.True(mapped.IsSuccess);
        Assert.Equal(84, mapped.Value);
    }

    // =========================================================================
    // BindAsync Tests
    // =========================================================================

    [Fact]
    public async Task BindAsync_Task_ChainsWhenSuccess()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));

        var bound = await resultTask.BindAsync(v =>
            Result<string, StringError>.Success($"value: {v}"));

        Assert.True(bound.IsSuccess);
        Assert.Equal("value: 42", bound.Value);
    }

    [Fact]
    public async Task BindAsync_Task_PreservesErrorWhenFailure()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Failure(new StringError("original")));

        var bound = await resultTask.BindAsync(v =>
            Result<string, StringError>.Success($"value: {v}"));

        Assert.True(bound.IsFailure);
        Assert.Equal("original", bound.Error.Message);
    }

    [Fact]
    public async Task BindAsync_AsyncBinder_ChainsWhenSuccess()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));

        var bound = await resultTask.BindAsync(async v =>
        {
            await Task.Delay(1);
            return Result<string, StringError>.Success($"value: {v}");
        });

        Assert.True(bound.IsSuccess);
        Assert.Equal("value: 42", bound.Value);
    }

    [Fact]
    public async Task BindAsync_Result_AsyncBinder_ChainsWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);

        var bound = await result.BindAsync(async v =>
        {
            await Task.Delay(1);
            return Result<string, StringError>.Success($"value: {v}");
        });

        Assert.True(bound.IsSuccess);
        Assert.Equal("value: 42", bound.Value);
    }

    // =========================================================================
    // MatchAsync Tests
    // =========================================================================

    [Fact]
    public async Task MatchAsync_CallsOnSuccessWhenSuccess()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));

        var output = await resultTask.MatchAsync(
            v => $"value: {v}",
            e => $"error: {e.Message}");

        Assert.Equal("value: 42", output);
    }

    [Fact]
    public async Task MatchAsync_CallsOnFailureWhenFailure()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Failure(new StringError("error")));

        var output = await resultTask.MatchAsync(
            v => $"value: {v}",
            e => $"error: {e.Message}");

        Assert.Equal("error: error", output);
    }

    // =========================================================================
    // TapAsync Tests
    // =========================================================================

    [Fact]
    public async Task TapAsync_Result_ExecutesActionWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);
        var executed = false;

        var tapped = await result.TapAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.True(executed);
        Assert.True(tapped.IsSuccess);
        Assert.Equal(42, tapped.Value);
    }

    [Fact]
    public async Task TapAsync_Result_DoesNotExecuteActionWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));
        var executed = false;

        var tapped = await result.TapAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.False(executed);
        Assert.True(tapped.IsFailure);
    }

    [Fact]
    public async Task TapAsync_Task_ExecutesActionWhenSuccess()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));
        var executed = false;

        var tapped = await resultTask.TapAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.True(executed);
        Assert.True(tapped.IsSuccess);
    }

    // =========================================================================
    // OnFailureAsync Tests
    // =========================================================================

    [Fact]
    public async Task OnFailureAsync_Result_ExecutesActionWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));
        StringError? capturedError = null;

        var handled = await result.OnFailureAsync(async e =>
        {
            await Task.Delay(1);
            capturedError = e;
        });

        Assert.NotNull(capturedError);
        Assert.Equal("error", capturedError?.Message);
        Assert.True(handled.IsFailure);
    }

    [Fact]
    public async Task OnFailureAsync_Result_DoesNotExecuteActionWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);
        var executed = false;

        var handled = await result.OnFailureAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.False(executed);
        Assert.True(handled.IsSuccess);
    }

    [Fact]
    public async Task OnFailureAsync_Task_ExecutesActionWhenFailure()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Failure(new StringError("error")));
        var executed = false;

        var handled = await resultTask.OnFailureAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.True(executed);
        Assert.True(handled.IsFailure);
    }

    // =========================================================================
    // EnsureAsync Tests
    // =========================================================================

    [Fact]
    public async Task EnsureAsync_Result_ReturnsSuccessWhenPredicatePasses()
    {
        var result = Result<int, StringError>.Success(42);

        var ensured = await result.EnsureAsync(
            async v =>
            {
                await Task.Delay(1);
                return v > 0;
            },
            v => new StringError($"{v} is not positive"));

        Assert.True(ensured.IsSuccess);
        Assert.Equal(42, ensured.Value);
    }

    [Fact]
    public async Task EnsureAsync_Result_ReturnsFailureWhenPredicateFails()
    {
        var result = Result<int, StringError>.Success(-5);

        var ensured = await result.EnsureAsync(
            async v =>
            {
                await Task.Delay(1);
                return v > 0;
            },
            v => new StringError($"{v} is not positive"));

        Assert.True(ensured.IsFailure);
        Assert.Equal("-5 is not positive", ensured.Error.Message);
    }

    [Fact]
    public async Task EnsureAsync_Result_PreservesOriginalErrorWhenAlreadyFailed()
    {
        var result = Result<int, StringError>.Failure(new StringError("original error"));

        var ensured = await result.EnsureAsync(
            async v =>
            {
                await Task.Delay(1);
                return v > 0;
            },
            v => new StringError("should not see this"));

        Assert.True(ensured.IsFailure);
        Assert.Equal("original error", ensured.Error.Message);
    }

    [Fact]
    public async Task EnsureAsync_Task_ReturnsSuccessWhenPredicatePasses()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));

        var ensured = await resultTask.EnsureAsync(
            async v =>
            {
                await Task.Delay(1);
                return v > 0;
            },
            v => new StringError($"{v} is not positive"));

        Assert.True(ensured.IsSuccess);
    }

    // =========================================================================
    // OrElseAsync Tests
    // =========================================================================

    [Fact]
    public async Task OrElseAsync_Result_ReturnsOriginalWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);

        var recovered = await result.OrElseAsync(async _ =>
        {
            await Task.Delay(1);
            return Result<int, StringError>.Success(0);
        });

        Assert.True(recovered.IsSuccess);
        Assert.Equal(42, recovered.Value);
    }

    [Fact]
    public async Task OrElseAsync_Result_ReturnsFallbackWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));

        var recovered = await result.OrElseAsync(async _ =>
        {
            await Task.Delay(1);
            return Result<int, StringError>.Success(0);
        });

        Assert.True(recovered.IsSuccess);
        Assert.Equal(0, recovered.Value);
    }

    [Fact]
    public async Task OrElseAsync_Task_ReturnsOriginalWhenSuccess()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));

        var recovered = await resultTask.OrElseAsync(async _ =>
        {
            await Task.Delay(1);
            return Result<int, StringError>.Success(0);
        });

        Assert.True(recovered.IsSuccess);
        Assert.Equal(42, recovered.Value);
    }

    [Fact]
    public async Task OrElseAsync_Task_ReturnsFallbackWhenFailure()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Failure(new StringError("error")));

        var recovered = await resultTask.OrElseAsync(async _ =>
        {
            await Task.Delay(1);
            return Result<int, StringError>.Success(-1);
        });

        Assert.True(recovered.IsSuccess);
        Assert.Equal(-1, recovered.Value);
    }

    // =========================================================================
    // VoidResult TapAsync Tests
    // =========================================================================

    [Fact]
    public async Task TapAsync_VoidResult_ExecutesActionWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();
        var executed = false;

        var tapped = await result.TapAsync(async () =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.True(executed);
        Assert.True(tapped.IsSuccess);
    }

    [Fact]
    public async Task TapAsync_VoidResult_DoesNotExecuteActionWhenFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));
        var executed = false;

        var tapped = await result.TapAsync(async () =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.False(executed);
        Assert.True(tapped.IsFailure);
    }

    [Fact]
    public async Task TapAsync_TaskVoidResult_ExecutesActionWhenSuccess()
    {
        var resultTask = Task.FromResult(VoidResult<StringError>.Success());
        var executed = false;

        var tapped = await resultTask.TapAsync(async () =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.True(executed);
        Assert.True(tapped.IsSuccess);
    }

    // =========================================================================
    // VoidResult OnFailureAsync Tests
    // =========================================================================

    [Fact]
    public async Task OnFailureAsync_VoidResult_ExecutesActionWhenFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));
        StringError? capturedError = null;

        var handled = await result.OnFailureAsync(async e =>
        {
            await Task.Delay(1);
            capturedError = e;
        });

        Assert.NotNull(capturedError);
        Assert.Equal("error", capturedError?.Message);
        Assert.True(handled.IsFailure);
    }

    [Fact]
    public async Task OnFailureAsync_VoidResult_DoesNotExecuteActionWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();
        var executed = false;

        var handled = await result.OnFailureAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        Assert.False(executed);
        Assert.True(handled.IsSuccess);
    }

    // =========================================================================
    // VoidResult OrElseAsync Tests
    // =========================================================================

    [Fact]
    public async Task OrElseAsync_VoidResult_ReturnsOriginalWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();

        var recovered = await result.OrElseAsync(async _ =>
        {
            await Task.Delay(1);
            return VoidResult<StringError>.Success();
        });

        Assert.True(recovered.IsSuccess);
    }

    [Fact]
    public async Task OrElseAsync_VoidResult_ReturnsFallbackWhenFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));

        var recovered = await result.OrElseAsync(async _ =>
        {
            await Task.Delay(1);
            return VoidResult<StringError>.Success();
        });

        Assert.True(recovered.IsSuccess);
    }

    // =========================================================================
    // ThenAsync Tests
    // =========================================================================

    [Fact]
    public async Task ThenAsync_ChainsWhenSuccess()
    {
        var resultTask = Task.FromResult(VoidResult<StringError>.Success());
        var nextCalled = false;

        var chained = await resultTask.ThenAsync(async () =>
        {
            await Task.Delay(1);
            nextCalled = true;
            return VoidResult<StringError>.Success();
        });

        Assert.True(nextCalled);
        Assert.True(chained.IsSuccess);
    }

    [Fact]
    public async Task ThenAsync_DoesNotChainWhenFailure()
    {
        var resultTask = Task.FromResult(VoidResult<StringError>.Failure(new StringError("error")));
        var nextCalled = false;

        var chained = await resultTask.ThenAsync(async () =>
        {
            await Task.Delay(1);
            nextCalled = true;
            return VoidResult<StringError>.Success();
        });

        Assert.False(nextCalled);
        Assert.True(chained.IsFailure);
        Assert.Equal("error", chained.Error.Message);
    }

    // =========================================================================
    // Cancellation Tests
    // =========================================================================

    [Fact]
    public async Task MapAsync_ThrowsWhenCancelled()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            resultTask.MapAsync(v => v * 2, cts.Token));
    }

    [Fact]
    public async Task BindAsync_ThrowsWhenCancelled()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            resultTask.BindAsync(v => Result<string, StringError>.Success(v.ToString()), cts.Token));
    }

    [Fact]
    public async Task TapAsync_ThrowsWhenCancelled()
    {
        var result = Result<int, StringError>.Success(42);
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            result.TapAsync(async _ => await Task.Delay(1), cts.Token));
    }

    [Fact]
    public async Task EnsureAsync_ThrowsWhenCancelled()
    {
        var result = Result<int, StringError>.Success(42);
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            result.EnsureAsync(async _ =>
            {
                await Task.Delay(1);
                return true;
            }, _ => new StringError("error"), cts.Token));
    }

    [Fact]
    public async Task OrElseAsync_ThrowsWhenCancelled()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            result.OrElseAsync(async _ =>
            {
                await Task.Delay(1);
                return Result<int, StringError>.Success(0);
            }, cts.Token));
    }
}