// =============================================================================
// OnSuccessAsync extension method unit tests
// =============================================================================

using Pragmatic.Testing.Assertions;
using Pragmatic.Result.Extensions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class OnSuccessAsyncExtensionsTests
{
    // =========================================================================
    // Result<T,E> OnSuccessAsync — from Result
    // =========================================================================

    [Fact]
    public async Task OnSuccessAsync_Result_ExecutesActionWhenSuccess()
    {
        var result = Result<int, StringError>.Success(42);
        var executed = false;
        int? capturedValue = null;

        var returned = await result.OnSuccessAsync(async v =>
        {
            await Task.Delay(1);
            executed = true;
            capturedValue = v;
        });

        executed.Should().BeTrue();
        capturedValue.Should().Be(42);
        returned.IsSuccess.Should().BeTrue();
        returned.Value.Should().Be(42);
    }

    [Fact]
    public async Task OnSuccessAsync_Result_DoesNotExecuteActionWhenFailure()
    {
        var result = Result<int, StringError>.Failure(new StringError("error"));
        var executed = false;

        var returned = await result.OnSuccessAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        executed.Should().BeFalse();
        returned.IsFailure.Should().BeTrue();
        returned.Error.Message.Should().Be("error");
    }

    [Fact]
    public async Task OnSuccessAsync_Result_ThrowsWhenCancelled()
    {
        var result = Result<int, StringError>.Success(42);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => result.OnSuccessAsync(async _ => await Task.Delay(1), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // Result<T,E> OnSuccessAsync — from Task<Result>
    // =========================================================================

    [Fact]
    public async Task OnSuccessAsync_Task_ExecutesActionWhenSuccess()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));
        var executed = false;

        var returned = await resultTask.OnSuccessAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        executed.Should().BeTrue();
        returned.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task OnSuccessAsync_Task_DoesNotExecuteActionWhenFailure()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Failure(new StringError("error")));
        var executed = false;

        var returned = await resultTask.OnSuccessAsync(async _ =>
        {
            await Task.Delay(1);
            executed = true;
        });

        executed.Should().BeFalse();
        returned.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task OnSuccessAsync_Task_ThrowsWhenCancelled()
    {
        var resultTask = Task.FromResult(Result<int, StringError>.Success(42));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => resultTask.OnSuccessAsync(async _ => await Task.Delay(1), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // VoidResult<E> OnSuccessAsync — from VoidResult
    // =========================================================================

    [Fact]
    public async Task OnSuccessAsync_VoidResult_ExecutesActionWhenSuccess()
    {
        var result = VoidResult<StringError>.Success();
        var executed = false;

        var returned = await result.OnSuccessAsync(async () =>
        {
            await Task.Delay(1);
            executed = true;
        });

        executed.Should().BeTrue();
        returned.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task OnSuccessAsync_VoidResult_DoesNotExecuteActionWhenFailure()
    {
        var result = VoidResult<StringError>.Failure(new StringError("error"));
        var executed = false;

        var returned = await result.OnSuccessAsync(async () =>
        {
            await Task.Delay(1);
            executed = true;
        });

        executed.Should().BeFalse();
        returned.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task OnSuccessAsync_VoidResult_ThrowsWhenCancelled()
    {
        var result = VoidResult<StringError>.Success();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => result.OnSuccessAsync(async () => await Task.Delay(1), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // VoidResult<E> OnSuccessAsync — from Task<VoidResult>
    // =========================================================================

    [Fact]
    public async Task OnSuccessAsync_TaskVoidResult_ExecutesActionWhenSuccess()
    {
        var resultTask = Task.FromResult(VoidResult<StringError>.Success());
        var executed = false;

        var returned = await resultTask.OnSuccessAsync(async () =>
        {
            await Task.Delay(1);
            executed = true;
        });

        executed.Should().BeTrue();
        returned.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task OnSuccessAsync_TaskVoidResult_DoesNotExecuteActionWhenFailure()
    {
        var resultTask = Task.FromResult(VoidResult<StringError>.Failure(new StringError("error")));
        var executed = false;

        var returned = await resultTask.OnSuccessAsync(async () =>
        {
            await Task.Delay(1);
            executed = true;
        });

        executed.Should().BeFalse();
        returned.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task OnSuccessAsync_TaskVoidResult_ThrowsWhenCancelled()
    {
        var resultTask = Task.FromResult(VoidResult<StringError>.Success());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => resultTask.OnSuccessAsync(async () => await Task.Delay(1), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =========================================================================
    // OnSuccessAsync behaves identically to TapAsync
    // =========================================================================

    [Fact]
    public async Task OnSuccessAsync_BehavesIdenticallyToTapAsync()
    {
        var result = Result<string, StringError>.Success("test");
        string? tapValue = null;
        string? onSuccessValue = null;

        await result.TapAsync(async v =>
        {
            await Task.Delay(1);
            tapValue = v;
        });

        await result.OnSuccessAsync(async v =>
        {
            await Task.Delay(1);
            onSuccessValue = v;
        });

        tapValue.Should().Be(onSuccessValue);
    }
}
