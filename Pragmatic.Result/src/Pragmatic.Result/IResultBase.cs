namespace Pragmatic.Result;

/// <summary>
///     Marker interface for all Result types to enable runtime identification and conversion.
/// </summary>
/// <remarks>
///     <para>
///         This interface is used by ASP.NET Core filters to automatically convert
///         Result types to appropriate HTTP responses (IResult for Minimal APIs, IActionResult for Controllers).
///     </para>
///     <para>
///         Implemented by: <see cref="Result{TValue, TError}" />, <see cref="Result{TValue}" />
///         (untyped error), <c>VoidResult</c>, <see cref="VoidResult{TError}" />, and every
///         source-generated multi-error variant (<c>Result&lt;TValue, E1..E8&gt;</c> /
///         <c>VoidResult&lt;E1..E8&gt;</c>).
///     </para>
/// </remarks>
public interface IResultBase
{
    /// <summary>
    ///     Gets whether this result represents a successful operation.
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    ///     Gets whether this result represents a failed operation.
    /// </summary>
    bool IsFailure { get; }

    /// <summary>
    ///     Gets whether this result is a value-bearing result rather than a void result.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Returns <c>true</c> for value-bearing results (<see cref="Result{TValue}" /> /
    ///         <see cref="Result{TValue, TError}" />) and <c>false</c> for void results
    ///         (<c>VoidResult</c> / <see cref="VoidResult{TError}" />).
    ///     </para>
    ///     <para>
    ///         IMPORTANT: despite the name, this property does NOT describe whether <c>TValue</c>
    ///         is a value type vs. a reference type. It only indicates whether the result shape
    ///         carries a value at all. A value-bearing result whose <c>TValue</c> is a reference
    ///         type still returns <c>true</c> here.
    ///     </para>
    ///     <para>
    ///         Use this to distinguish a void success (no value expected) from a value-bearing
    ///         success whose <c>Value</c> happens to be <c>null</c>.
    ///     </para>
    /// </remarks>
    bool HasValueType { get; }

    /// <summary>
    ///     Gets the success value as object, or null if failed or void.
    /// </summary>
    object? ValueAsObject { get; }

    /// <summary>
    ///     Gets the error as IError interface, or null if successful.
    /// </summary>
    IError? ErrorAsObject { get; }
}