namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     Interface for responses that support ETag-based caching.
/// </summary>
/// <remarks>
///     <para>
///         Implement this interface on your response DTOs to enable automatic
///         ETag handling for conditional GET requests.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public record UserResponse : IETagSupport
/// {
///     public Guid Id { get; init; }
///     public string Name { get; init; }
///     public string? ETag { get; set; }
/// }
/// </code>
/// </example>
public interface IETagSupport
{
    /// <summary>
    ///     Gets or sets the ETag value for this response.
    /// </summary>
    string? ETag { get; set; }
}