using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     Every query the application sends to the database, as its text: what a read costs is decided by the
///     SQL, and an HTTP test that only reads the response cannot tell one statement from a page's worth.
/// </summary>
/// <remarks>
///     Registered as an <see cref="IInterceptor" /> in the test host's container, which is where the
///     application's contexts pick their interceptors up. It observes and changes nothing.
/// </remarks>
public sealed class SqlCapture : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    /// <summary>The queries sent since the last <see cref="Clear" />, in order.</summary>
    public IReadOnlyList<string> Commands => [.. _commands];

    public void Clear() => _commands.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        _commands.Enqueue(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }
}
