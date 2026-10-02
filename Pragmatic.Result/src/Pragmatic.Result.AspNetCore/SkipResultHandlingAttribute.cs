namespace Pragmatic.Result.AspNetCore;

/// <summary>
///     Indicates that automatic Result-to-HTTP conversion should be skipped for this endpoint/action.
/// </summary>
/// <remarks>
///     <para>
///         Apply this attribute to opt-out of automatic Result handling when using
///         <see cref="ResultEndpointFilter" /> or <see cref="ResultActionFilter" />.
///     </para>
///     <para>
///         Useful when you need custom response handling for specific endpoints.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Minimal API - skip Result handling for this endpoint
/// app.MapGet("/special", [SkipResultHandling] () => ...);
/// 
/// // Controller - skip Result handling for this action
/// [SkipResultHandling]
/// [HttpGet("special")]
/// public async Task&lt;Result&lt;Data, Error&gt;&gt; Special() => ...;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SkipResultHandlingAttribute : Attribute
{
}