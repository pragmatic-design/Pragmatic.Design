// =============================================================================
// Result static factory methods unit tests (Try, TryAsync, FromNullable)
// =============================================================================

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class ResultFactoryTests
{
    // =========================================================================
    // Try<TValue, TError> (Sync with return value)
    // =========================================================================

    [Fact]
    public void Try_WhenOperationSucceeds_ReturnsSuccess()
    {
        var result = Result.Try(
            () => 42,
            ex => new StringError(ex.Message));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Try_WhenOperationThrows_ReturnsFailure()
    {
        var result = Result.Try<int, StringError>(
            () => throw new InvalidOperationException("test error"),
            ex => new StringError(ex.Message));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("test error");
    }

    [Fact]
    public void Try_WhenOperationThrows_PassesExceptionToMapper()
    {
        Exception? capturedEx = null;

        Result.Try<int, StringError>(
            () => throw new InvalidOperationException("test error"),
            ex =>
            {
                capturedEx = ex;
                return new StringError(ex.Message);
            });

        capturedEx.Should().NotBeNull();
        capturedEx.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void Try_ThrowsOnNullOperation()
    {
        var act = () => Result.Try<int, StringError>(null!, ex => new StringError(ex.Message));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Try_ThrowsOnNullErrorMapper()
    {
        var act = () => Result.Try(() => 42, (Func<Exception, StringError>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Try_CapturesInnerException()
    {
        var innerEx = new ArgumentException("inner");

        var result = Result.Try<int, StringError>(
            () => throw new InvalidOperationException("outer", innerEx),
            ex => new StringError(ex.InnerException?.Message ?? "no inner"));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("inner");
    }

    // =========================================================================
    // TryAsync<TValue, TError> (Async with return value)
    // =========================================================================

    [Fact]
    public async Task TryAsync_WhenOperationSucceeds_ReturnsSuccess()
    {
        var result = await Result.TryAsync(
            async ct =>
            {
                await Task.Delay(1, ct);
                return 42;
            },
            ex => new StringError(ex.Message));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task TryAsync_WhenOperationThrows_ReturnsFailure()
    {
        var result = await Result.TryAsync<int, StringError>(
            ct => throw new InvalidOperationException("async error"),
            ex => new StringError(ex.Message));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("async error");
    }

    [Fact]
    public async Task TryAsync_WhenOperationThrowsAfterAwait_ReturnsFailure()
    {
        var result = await Result.TryAsync<int, StringError>(
            async ct =>
            {
                await Task.Delay(1, ct);
                throw new InvalidOperationException("async error after delay");
            },
            ex => new StringError(ex.Message));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("async error after delay");
    }

    [Fact]
    public async Task TryAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken receivedToken = default;

        await Result.TryAsync(
            ct =>
            {
                receivedToken = ct;
                return Task.FromResult(42);
            },
            ex => new StringError(ex.Message),
            cts.Token);

        receivedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task TryAsync_WhenCancelled_CapturesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await Result.TryAsync<int, StringError>(
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                return await Task.FromResult(42);
            },
            ex => new StringError(ex.GetType().Name),
            cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Cancel");
    }

    [Fact]
    public async Task TryAsync_ThrowsOnNullOperation()
    {
        var act = () => Result.TryAsync<int, StringError>(null!, ex => new StringError(ex.Message));
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TryAsync_ThrowsOnNullErrorMapper()
    {
        var act = () => Result.TryAsync(ct => Task.FromResult(42), (Func<Exception, StringError>)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // =========================================================================
    // Try<TError> (Sync void operation)
    // =========================================================================

    [Fact]
    public void TryVoid_WhenOperationSucceeds_ReturnsSuccess()
    {
        var executed = false;

        var result = Result.Try(
            () => executed = true,
            ex => new StringError(ex.Message));

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public void TryVoid_WhenOperationThrows_ReturnsFailure()
    {
        var result = Result.Try<StringError>(
            () => throw new InvalidOperationException("void error"),
            ex => new StringError(ex.Message));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("void error");
    }

    [Fact]
    public void TryVoid_ThrowsOnNullAction()
    {
        var act = () => Result.Try(null!, ex => new StringError(ex.Message));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryVoid_ThrowsOnNullErrorMapper()
    {
        var act = () => Result.Try(() => { }, (Func<Exception, StringError>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // TryAsync<TError> (Async void operation)
    // =========================================================================

    [Fact]
    public async Task TryAsyncVoid_WhenOperationSucceeds_ReturnsSuccess()
    {
        var executed = false;

        var result = await Result.TryAsync(
            async ct =>
            {
                await Task.Delay(1, ct);
                executed = true;
            },
            ex => new StringError(ex.Message));

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task TryAsyncVoid_WhenOperationThrows_ReturnsFailure()
    {
        var result = await Result.TryAsync<StringError>(
            ct => throw new InvalidOperationException("async void error"),
            ex => new StringError(ex.Message));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("async void error");
    }

    [Fact]
    public async Task TryAsyncVoid_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken receivedToken = default;

        await Result.TryAsync(
            ct =>
            {
                receivedToken = ct;
                return Task.CompletedTask;
            },
            ex => new StringError(ex.Message),
            cts.Token);

        receivedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task TryAsyncVoid_ThrowsOnNullOperation()
    {
        var act = () => Result.TryAsync<StringError>(
            (Func<CancellationToken, Task>)null!, ex => new StringError(ex.Message));
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // =========================================================================
    // FromNullable — Reference types
    // =========================================================================

    [Fact]
    public void FromNullable_ReferenceType_NonNull_ReturnsSuccess()
    {
        var result = Result.FromNullable<string, StringError>("hello", new StringError("Not found"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void FromNullable_ReferenceType_Null_ReturnsFailure()
    {
        var error = new StringError("Not found");
        var result = Result.FromNullable<string, StringError>(null, error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void FromNullable_ReferenceType_WithFactory_NonNull_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.FromNullable<string, StringError>("hello", () =>
        {
            factoryCalled = true;
            return new StringError("Not found");
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public void FromNullable_ReferenceType_WithFactory_Null_CallsFactory()
    {
        var error = new StringError("Not found");
        var result = Result.FromNullable<string, StringError>(null, () => error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void FromNullable_ReferenceType_WithFactory_ThrowsOnNullFactory()
    {
        var act = () => Result.FromNullable<string, StringError>(null, (Func<StringError>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // FromNullable — Value types
    // =========================================================================

    [Fact]
    public void FromNullable_ValueType_HasValue_ReturnsSuccess()
    {
        int? value = 42;
        var result = Result.FromNullable<int, StringError>(value, new StringError("Not found"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void FromNullable_ValueType_Null_ReturnsFailure()
    {
        int? value = null;
        var error = new StringError("Not found");
        var result = Result.FromNullable<int, StringError>(value, error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void FromNullable_ValueType_WithFactory_HasValue_DoesNotCallFactory()
    {
        var factoryCalled = false;
        Guid? value = Guid.NewGuid();

        var result = Result.FromNullable<Guid, StringError>(value, () =>
        {
            factoryCalled = true;
            return new StringError("Not found");
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public void FromNullable_ValueType_WithFactory_Null_CallsFactory()
    {
        DateTime? value = null;
        var error = new StringError("Not found");
        var result = Result.FromNullable<DateTime, StringError>(value, () => error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void FromNullable_ValueType_WithFactory_ThrowsOnNullFactory()
    {
        int? value = null;
        var act = () => Result.FromNullable<int, StringError>(value, (Func<StringError>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // FromNullable — Edge cases
    // =========================================================================

    [Fact]
    public void FromNullable_ReferenceType_EmptyString_ReturnsSuccess()
    {
        var result = Result.FromNullable<string, StringError>("", new StringError("Not found"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void FromNullable_ValueType_Zero_ReturnsSuccess()
    {
        int? value = 0;
        var result = Result.FromNullable<int, StringError>(value, new StringError("Not found"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public void FromNullable_ValueType_DefaultGuid_ReturnsSuccess()
    {
        Guid? value = Guid.Empty;
        var result = Result.FromNullable<Guid, StringError>(value, new StringError("Not found"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Guid.Empty);
    }

    // =========================================================================
    // Try — Edge cases
    // =========================================================================

    [Fact]
    public void Try_WithDifferentExceptionTypes_MapsCorrectly()
    {
        var result1 = Result.Try<int, StringError>(
            () => throw new ArgumentNullException("param"),
            ex => new StringError(ex.GetType().Name));
        result1.Error.Message.Should().Be("ArgumentNullException");

        var result2 = Result.Try<int, StringError>(
            () => throw new FormatException("bad format"),
            ex => new StringError(ex.GetType().Name));
        result2.Error.Message.Should().Be("FormatException");
    }

    [Fact]
    public void Try_PreservesExceptionData()
    {
        var ex = new InvalidOperationException("error");
        ex.Data["key"] = "value";

        var result = Result.Try<int, StringError>(
            () => throw ex,
            caught => new StringError((string?)caught.Data["key"] ?? "no data"));

        result.Error.Message.Should().Be("value");
    }

    [Fact]
    public async Task TryAsync_WithAggregateException_CapturesInner()
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

        result.IsFailure.Should().BeTrue();
    }
}
