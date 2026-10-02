namespace Pragmatic.Endpoints.Samples.Services;

/// <summary>
///     Repository interface for user operations.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    ///     Gets a user by ID.
    /// </summary>
    Task<UserEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    ///     Gets all users with pagination.
    /// </summary>
    Task<IReadOnlyList<UserEntity>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    ///     Creates a new user.
    /// </summary>
    Task<UserEntity> CreateAsync(string name, string email, CancellationToken ct = default);

    /// <summary>
    ///     Updates an existing user.
    /// </summary>
    Task<UserEntity?> UpdateAsync(Guid id, string name, string email, CancellationToken ct = default);

    /// <summary>
    ///     Deletes a user.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
///     User entity.
/// </summary>
public record UserEntity(Guid Id, string Name, string Email, DateTimeOffset CreatedAt);