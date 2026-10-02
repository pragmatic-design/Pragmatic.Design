using Pragmatic.Resilience.Errors;
using Pragmatic.Resilience.Strategies;
using Pragmatic.Result;

namespace Pragmatic.Resilience.Bridge;

/// <summary>
/// Bridges resilience pipeline execution with the Result pattern,
/// converting resilience exceptions into typed errors.
/// </summary>
public static class ResilienceResultBridge
{
    /// <summary>
    ///     Sentinel <see cref="ResilienceContext.OperationName"/> assigned when the
    ///     caller invokes the non-generic (void) <c>ExecuteAsResultAsync</c> overload
    ///     and does not supply a name. Centralised so telemetry consumers can filter
    ///     these out instead of treating each magic string occurrence as a hot path.
    /// </summary>
    public const string VoidOperationName = "void";

    /// <summary>
    ///     Maps a resilience strategy exception to its corresponding typed <see cref="IError"/>.
    ///     Returns <c>true</c> and sets <paramref name="error"/> for any of the six resilience
    ///     exceptions (circuit-broken, bulkhead-rejected, timeout, retry-exhausted, hedging-exhausted,
    ///     rate-limit-rejected); returns <c>false</c> for every other exception, which the caller must
    ///     let propagate. This is the single mapping table shared by the Result bridge and by the
    ///     source-generated action invoker, so both surface resilience failures as typed errors
    ///     (with the correct HTTP status) rather than raw exceptions.
    /// </summary>
    public static bool TryMapToError(Exception exception, out IError error)
    {
        switch (exception)
        {
            case CircuitBrokenException ex:
                error = new CircuitBrokenError(ex.CircuitKey, ex.BreakDuration);
                return true;
            case BulkheadRejectedException ex:
                error = new BulkheadRejectedError(ex.OperationName, ex.MaxConcurrency);
                return true;
            case TimeoutRejectedException ex:
                error = new TimeoutError(ex.OperationName, ex.Timeout);
                return true;
            case RetryExhaustedException ex:
                error = new RetryExhaustedError(ex.OperationName, ex.Attempts, ex.InnerException);
                return true;
            case HedgingExhaustedException ex:
                error = new HedgingExhaustedError(ex.MaxAttempts);
                return true;
            case RateLimitRejectedException ex:
                error = new RateLimitRejectedError(ex.MaxRequests, ex.Window);
                return true;
            default:
                error = null!;
                return false;
        }
    }

    extension(IResiliencePipeline pipeline)
    {
        /// <summary>
        /// Executes an operation through the pipeline, returning a Result instead of throwing.
        /// </summary>
        public async Task<Result<T, IError>> ExecuteAsResultAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            try
            {
                var context = new ResilienceContext { OperationName = typeof(T).Name };
                var result = await pipeline.ExecuteAsync(
                    (_, token) => operation(token), context, ct).ConfigureAwait(false);
                return Result<T, IError>.Success(result);
            }
            catch (Exception ex) when (TryMapToError(ex, out var error))
            {
                return Result<T, IError>.Failure(error);
            }
        }

        /// <summary>
        /// Executes a void operation through the pipeline, returning a VoidResult instead of throwing.
        /// </summary>
        public async Task<VoidResult<IError>> ExecuteAsResultAsync(Func<CancellationToken, Task> operation,
            CancellationToken ct = default)
        {
            try
            {
                var context = new ResilienceContext { OperationName = VoidOperationName };
                await pipeline.ExecuteAsync(
                    (_, token) => operation(token), context, ct).ConfigureAwait(false);
                return VoidResult<IError>.Success();
            }
            catch (Exception ex) when (TryMapToError(ex, out var error))
            {
                return VoidResult<IError>.Failure(error);
            }
        }
    }
}
