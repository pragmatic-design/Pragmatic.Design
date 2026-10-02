using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Endpoints.Processors;

/// <summary>
///     Result of a pre-processor execution.
/// </summary>
/// <remarks>
///     <para>
///         A pre-processor can either continue to the next processor/handler
///         or short-circuit by returning an error.
///     </para>
/// </remarks>
public readonly struct PreProcessorResult
{
    private PreProcessorResult(bool shouldContinue, IError? error = null)
    {
        ShouldContinue = shouldContinue;
        Error = error;
    }

    /// <summary>
    ///     Gets whether processing should continue to the next processor/handler.
    /// </summary>
    public bool ShouldContinue { get; }

    /// <summary>
    ///     Gets the error if processing should stop.
    /// </summary>
    public IError? Error { get; }

    /// <summary>
    ///     Creates a result indicating processing should continue.
    /// </summary>
    public static PreProcessorResult Continue()
    {
        return new PreProcessorResult(true);
    }

    /// <summary>
    ///     Creates a result indicating processing should stop with an error.
    /// </summary>
    /// <param name="error">The error to return.</param>
    public static PreProcessorResult Fail(IError error)
    {
        return new PreProcessorResult(false, error);
    }

    /// <summary>
    ///     Creates a result indicating processing should stop with a not found error.
    /// </summary>
    /// <param name="resourceType">The type of resource not found.</param>
    /// <param name="id">The identifier that was not found.</param>
    public static PreProcessorResult NotFound(string resourceType, string? id = null)
    {
        return new PreProcessorResult(false, NotFoundError.Create(resourceType, id));
    }

}