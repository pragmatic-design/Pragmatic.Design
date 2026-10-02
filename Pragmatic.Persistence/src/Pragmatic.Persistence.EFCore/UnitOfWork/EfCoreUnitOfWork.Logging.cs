using Microsoft.Extensions.Logging;

namespace Pragmatic.Persistence.EFCore.UnitOfWork;

/// <summary>
///     Source-generated structured logging for <see cref="EfCoreUnitOfWork"/>.
/// </summary>
public sealed partial class EfCoreUnitOfWork
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "SaveChangesAsync starting")]
    private partial void LogSaveChangesStarting();

    [LoggerMessage(Level = LogLevel.Debug, Message = "SaveChangesAsync completed: {rowCount} row(s) affected")]
    private partial void LogSaveChangesCompleted(int rowCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "SaveChangesAsync failed")]
    private partial void LogSaveChangesFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "BeginTransactionAsync starting")]
    private partial void LogBeginTransactionStarting();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Transaction {transactionId} started")]
    private partial void LogTransactionStarted(Guid transactionId);
}
