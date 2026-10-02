using Microsoft.Extensions.Logging;
using Pragmatic.Result;

namespace Pragmatic.Actions.Compensation;

/// <summary>
///     The per-request stack of undos. Not thread-safe, and deliberately so: an action's steps run in
///     sequence within one request scope, and a lock here would buy nothing while suggesting the
///     mechanism survives concurrency it does not.
/// </summary>
public sealed partial class CompensationScope(ILogger<CompensationScope> logger) : ICompensationScope
{
    private readonly List<CompensationEntry> _entries = [];

    /// <inheritdoc />
    public int Mark() => _entries.Count;

    /// <inheritdoc />
    public void Register(CompensationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }

    /// <inheritdoc />
    public async Task<CompensationFailure?> CompensateFromAsync(int mark, CancellationToken ct = default)
    {
        if (mark < 0 || mark >= _entries.Count)
            return null;

        // Taken and cleared before running anything: a compensator that itself invokes an action must
        // not see the entries it is in the middle of consuming, and a second failure further up the
        // stack must not run them twice.
        var pending = _entries.GetRange(mark, _entries.Count - mark);
        _entries.RemoveRange(mark, _entries.Count - mark);

        CompensationFailure? first = null;

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var entry = pending[i];
            IError? error;

            try
            {
                var result = await entry.Undo(ct).ConfigureAwait(false);
                error = result.IsSuccess ? null : result.Error;
            }
            catch (Exception ex)
            {
                // The undo threw. It is still a compensation failure, not an invocation failure: the
                // caller is owed the report, not a second exception on top of the one that started this.
                LogCompensationThrew(entry.ActionName, ex);
                error = new CompensatorThrewError { ActionName = entry.ActionName, Message = ex.Message };
            }

            if (error is null)
            {
                LogCompensated(entry.ActionName);
                continue;
            }

            LogCompensationFailed(entry.ActionName, error.Code);
            first ??= new CompensationFailure(entry.ActionName, error);
        }

        return first;
    }

    [LoggerMessage(EventId = 4240, Level = LogLevel.Information,
        Message = "Compensated committed work of {ActionName}")]
    private partial void LogCompensated(string actionName);

    [LoggerMessage(EventId = 4241, Level = LogLevel.Error,
        Message = "Compensation for {ActionName} failed with {ErrorCode}; its committed work remains")]
    private partial void LogCompensationFailed(string actionName, string errorCode);

    [LoggerMessage(EventId = 4242, Level = LogLevel.Error,
        Message = "Compensation for {ActionName} threw; its committed work remains")]
    private partial void LogCompensationThrew(string actionName, Exception exception);
}
