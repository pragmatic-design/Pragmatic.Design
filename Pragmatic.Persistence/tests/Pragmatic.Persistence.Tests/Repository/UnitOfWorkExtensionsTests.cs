using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Persistence.Repository;
using Pragmatic.Result;

namespace Pragmatic.Persistence.Tests.Repository;

/// <summary>
///     Tests for <see cref="UnitOfWorkExtensions" /> transaction scope helpers.
/// </summary>
public class UnitOfWorkExtensionsTests
{
    private readonly UnitOfWorkMock _uow = new UnitOfWorkMock();
    private readonly TransactionMock _tx = new TransactionMock();

    public UnitOfWorkExtensionsTests()
    {
        _tx.TransactionId.Returns(Guid.NewGuid());
        _uow.BeginTransactionAsync.Returns(_tx);

        // The unit runs once, as IUnitOfWork's default does: these tests are about the extensions'
        // commit and rollback. The retry is EfCoreUnitOfWork's, and is tested there.
        RunsOnce<Result<int, TestError>>();
        RunsOnce<VoidResult<TestError>>();
        RunsOnce<int>();
    }

    private void RunsOnce<T>()
        => _uow.ExecuteAsync.Returns<T>(arguments =>
            ((Func<CancellationToken, Task<T>>)arguments[0]!)((CancellationToken)arguments[1]!));

    /// <summary>
    ///     The transaction is opened inside the unit of work's retriable unit: a retrying execution strategy
    ///     refuses one opened outside it.
    /// </summary>
    [Fact]
    public async Task ExecuteInTransaction_OpensItsTransactionInsideExecuteAsync()
    {
        await _uow.ExecuteInTransactionAsync(_ => Task.FromResult(1));
        await _uow.ExecuteInTransactionAsync<int, TestError>(_ => Task.FromResult(Result<int, TestError>.Success(1)));
        await _uow.ExecuteInTransactionAsync<TestError>(_ => Task.FromResult(VoidResult<TestError>.Success()));

        _uow.ExecuteAsync.Received<int>(1);
        _uow.ExecuteAsync.Received<Result<int, TestError>>(1);
        _uow.ExecuteAsync.Received<VoidResult<TestError>>(1);
    }

    #region Test Error Type

    private sealed record TestError : Error
    {
        public override string Code => "TEST_ERROR";
        public override int StatusCode => 400;
    }

    #endregion

    // =========================================================================
    // ExecuteInTransactionAsync<T, TError> (Result overload)
    // =========================================================================

    #region Result<T, TError> overload

    [Fact]
    public async Task ExecuteInTransaction_Result_OnSuccess_CommitsTransaction()
    {
        // Act
        var result = await _uow.ExecuteInTransactionAsync<int, TestError>(
            _ => Task.FromResult(Result<int, TestError>.Success(42)));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        _tx.CommitAsync.Received(1);
        _tx.RollbackAsync.DidNotReceive();
    }

    [Fact]
    public async Task ExecuteInTransaction_Result_OnFailure_RollsBackTransaction()
    {
        // Arrange
        var error = new TestError();

        // Act
        var result = await _uow.ExecuteInTransactionAsync<int, TestError>(
            _ => Task.FromResult(Result<int, TestError>.Failure(error)));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        _tx.CommitAsync.DidNotReceive();
        _tx.RollbackAsync.Received(1);
    }

    [Fact]
    public async Task ExecuteInTransaction_Result_OnException_RollsBackAndRethrows()
    {
        // Arrange
        var exception = new InvalidOperationException("boom");

        // Act
        var act = () => _uow.ExecuteInTransactionAsync<int, TestError>(
            _ => throw exception);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        _tx.CommitAsync.DidNotReceive();
        _tx.RollbackAsync.Received(1);
    }

    [Fact]
    public async Task ExecuteInTransaction_Result_PassesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        // Act
        await _uow.ExecuteInTransactionAsync<int, TestError>(
            ct =>
            {
                capturedToken = ct;
                return Task.FromResult(Result<int, TestError>.Success(1));
            },
            cts.Token);

        // Assert
        capturedToken.Should().Be(cts.Token);
        _uow.BeginTransactionAsync.Received(1, cts.Token);
    }

    [Fact]
    public async Task ExecuteInTransaction_Result_NullUow_ThrowsArgumentNull()
    {
        // Act
        var act = () => UnitOfWorkExtensions.ExecuteInTransactionAsync<int, TestError>(
            null!,
            _ => Task.FromResult(Result<int, TestError>.Success(1)));

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("uow");
    }

    [Fact]
    public async Task ExecuteInTransaction_Result_NullOperation_ThrowsArgumentNull()
    {
        // Act
        var act = () => _uow.ExecuteInTransactionAsync<int, TestError>(
            (Func<CancellationToken, Task<Result<int, TestError>>>)null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("operation");
    }

    #endregion

    // =========================================================================
    // ExecuteInTransactionAsync<TError> (VoidResult overload)
    // =========================================================================

    #region VoidResult<TError> overload

    [Fact]
    public async Task ExecuteInTransaction_Void_OnSuccess_CommitsTransaction()
    {
        // Act
        var result = await _uow.ExecuteInTransactionAsync<TestError>(
            _ => Task.FromResult(VoidResult<TestError>.Success()));

        // Assert
        result.IsSuccess.Should().BeTrue();
        _tx.CommitAsync.Received(1);
        _tx.RollbackAsync.DidNotReceive();
    }

    [Fact]
    public async Task ExecuteInTransaction_Void_OnFailure_RollsBackTransaction()
    {
        // Arrange
        var error = new TestError();

        // Act
        var result = await _uow.ExecuteInTransactionAsync<TestError>(
            _ => Task.FromResult(VoidResult<TestError>.Failure(error)));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        _tx.CommitAsync.DidNotReceive();
        _tx.RollbackAsync.Received(1);
    }

    [Fact]
    public async Task ExecuteInTransaction_Void_OnException_RollsBackAndRethrows()
    {
        // Arrange
        var exception = new InvalidOperationException("void boom");

        // Typed, because `_ => throw exception` fits Func<CancellationToken, Task<T>> too: with the type
        // argument alone this bound to the raw overload with T = TestError, and tested that one.
        Func<CancellationToken, Task<VoidResult<TestError>>> operation = _ => throw exception;

        // Act
        var act = () => _uow.ExecuteInTransactionAsync(operation);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("void boom");
        _tx.CommitAsync.DidNotReceive();
        _tx.RollbackAsync.Received(1);
    }

    [Fact]
    public async Task ExecuteInTransaction_Void_PassesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        // Act
        await _uow.ExecuteInTransactionAsync<TestError>(
            ct =>
            {
                capturedToken = ct;
                return Task.FromResult(VoidResult<TestError>.Success());
            },
            cts.Token);

        // Assert
        capturedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task ExecuteInTransaction_Void_NullUow_ThrowsArgumentNull()
    {
        // Act
        var act = () => UnitOfWorkExtensions.ExecuteInTransactionAsync<TestError>(
            null!,
            _ => Task.FromResult(VoidResult<TestError>.Success()));

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("uow");
    }

    [Fact]
    public async Task ExecuteInTransaction_Void_NullOperation_ThrowsArgumentNull()
    {
        // Act
        var act = () => _uow.ExecuteInTransactionAsync(
            (Func<CancellationToken, Task<VoidResult<TestError>>>)null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("operation");
    }

    #endregion

    // =========================================================================
    // ExecuteInTransactionAsync<T> (raw value overload)
    // =========================================================================

    #region Raw value overload

    [Fact]
    public async Task ExecuteInTransaction_Raw_OnSuccess_CommitsAndReturnsValue()
    {
        // Act
        var result = await _uow.ExecuteInTransactionAsync(
            _ => Task.FromResult(42));

        // Assert
        result.Should().Be(42);
        _tx.CommitAsync.Received(1);
        _tx.RollbackAsync.DidNotReceive();
    }

    [Fact]
    public async Task ExecuteInTransaction_Raw_OnException_RollsBackAndRethrows()
    {
        // Arrange
        var exception = new InvalidOperationException("raw boom");

        // Act
        var act = () => _uow.ExecuteInTransactionAsync<int>(
            _ => throw exception);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("raw boom");
        _tx.CommitAsync.DidNotReceive();
        _tx.RollbackAsync.Received(1);
    }

    [Fact]
    public async Task ExecuteInTransaction_Raw_PassesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        // Act
        await _uow.ExecuteInTransactionAsync(
            ct =>
            {
                capturedToken = ct;
                return Task.FromResult(1);
            },
            cts.Token);

        // Assert
        capturedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task ExecuteInTransaction_Raw_NullUow_ThrowsArgumentNull()
    {
        // Act
        var act = () => UnitOfWorkExtensions.ExecuteInTransactionAsync<int>(
            null!,
            _ => Task.FromResult(1));

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("uow");
    }

    [Fact]
    public async Task ExecuteInTransaction_Raw_NullOperation_ThrowsArgumentNull()
    {
        // Act
        var act = () => _uow.ExecuteInTransactionAsync(
            (Func<CancellationToken, Task<int>>)null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("operation");
    }

    #endregion

    // =========================================================================
    // Transaction lifecycle: DisposeAsync is always called via await using
    // =========================================================================

    #region Transaction disposal

    [Fact]
    public async Task ExecuteInTransaction_Result_Success_DisposesTransaction()
    {
        // Act
        await _uow.ExecuteInTransactionAsync<int, TestError>(
            _ => Task.FromResult(Result<int, TestError>.Success(1)));

        // Assert
        _tx.DisposeAsync.Received(1);
    }

    [Fact]
    public async Task ExecuteInTransaction_Result_Failure_DisposesTransaction()
    {
        // Act
        await _uow.ExecuteInTransactionAsync<int, TestError>(
            _ => Task.FromResult(Result<int, TestError>.Failure(new TestError())));

        // Assert
        _tx.DisposeAsync.Received(1);
    }

    [Fact]
    public async Task ExecuteInTransaction_Result_Exception_DisposesTransaction()
    {
        // Act
        try
        {
            await _uow.ExecuteInTransactionAsync<int, TestError>(
                _ => throw new InvalidOperationException());
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        _tx.DisposeAsync.Received(1);
    }

    #endregion
}
