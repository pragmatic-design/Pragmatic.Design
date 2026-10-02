using Pragmatic.Result.Http;

namespace Pragmatic.Result.AspNetCore.Samples.Services;

/// <summary>
///     Sample user service demonstrating Result pattern with ASP.NET Core.
/// </summary>
public class UserService
{
    // In-memory "database" for demo purposes
    private static readonly Dictionary<int, User> Users = new()
    {
        [1] = new User(1, "Alice", "alice@example.com"),
        [2] = new User(2, "Bob", "bob@example.com"),
        [3] = new User(3, "Charlie", "charlie@example.com")
    };

    private static int _nextId = 4;

    /// <summary>
    ///     Gets a user by ID.
    ///     Returns NotFoundError if user doesn't exist.
    /// </summary>
    public Task<Result<User, NotFoundError>> GetByIdAsync(int id)
    {
        if (Users.TryGetValue(id, out var user))
            return Task.FromResult<Result<User, NotFoundError>>(user);

        return Task.FromResult<Result<User, NotFoundError>>(
            NotFoundError.Create("User", id));
    }

    /// <summary>
    ///     Creates a new user.
    ///     Returns ConflictError if email already exists.
    /// </summary>
    public Task<Result<User, ConflictError>> CreateAsync(CreateUserRequest request)
    {
        // Check for duplicate email
        if (Users.Values.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult<Result<User, ConflictError>>(
                ConflictError.AlreadyExists("User", request.Email));

        var user = new User(_nextId++, request.Name, request.Email);
        Users[user.Id] = user;

        return Task.FromResult<Result<User, ConflictError>>(user);
    }

    /// <summary>
    ///     Updates a user.
    ///     Returns NotFoundError if user doesn't exist.
    /// </summary>
    public Task<Result<User, NotFoundError>> UpdateAsync(int id, UpdateUserRequest request)
    {
        if (!Users.TryGetValue(id, out _))
            return Task.FromResult<Result<User, NotFoundError>>(
                NotFoundError.Create("User", id));

        var updatedUser = new User(id, request.Name, request.Email);
        Users[id] = updatedUser;

        return Task.FromResult<Result<User, NotFoundError>>(updatedUser);
    }

    /// <summary>
    ///     Deletes a user.
    ///     Returns NotFoundError if user doesn't exist.
    /// </summary>
    public Task<VoidResult<NotFoundError>> DeleteAsync(int id)
    {
        if (!Users.Remove(id))
            return Task.FromResult<VoidResult<NotFoundError>>(
                NotFoundError.Create("User", id));

        return Task.FromResult(VoidResult<NotFoundError>.Success());
    }
}

/// <summary>
///     User entity.
/// </summary>
public record User(int Id, string Name, string Email);

/// <summary>
///     Request to create a user.
/// </summary>
public record CreateUserRequest(string Name, string Email);

/// <summary>
///     Request to update a user.
/// </summary>
public record UpdateUserRequest(string Name, string Email);