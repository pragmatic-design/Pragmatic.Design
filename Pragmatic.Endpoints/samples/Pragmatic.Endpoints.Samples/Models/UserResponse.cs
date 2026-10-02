namespace Pragmatic.Endpoints.Samples.Models;

/// <summary>
///     Response containing user information.
/// </summary>
public sealed record UserResponse
{
    /// <summary>
    ///     The user's unique identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    ///     The user's name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The user's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    ///     When the user was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
}