using System.Collections.Concurrent;

namespace Pragmatic.Endpoints.Samples.Services;

/// <summary>
///     In-memory implementation of user repository for demo purposes.
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, UserEntity> _users = new();

    public InMemoryUserRepository()
    {
        // Seed some test data
        var user1 = new UserEntity(Guid.NewGuid(), "John Doe", "john@example.com", DateTimeOffset.UtcNow.AddDays(-30));
        var user2 = new UserEntity(Guid.NewGuid(), "Jane Smith", "jane@example.com",
            DateTimeOffset.UtcNow.AddDays(-15));
        _users.TryAdd(user1.Id, user1);
        _users.TryAdd(user2.Id, user2);
    }

    public Task<UserEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _users.TryGetValue(id, out var user);
        return Task.FromResult(user);
    }

    public Task<IReadOnlyList<UserEntity>> GetAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var users = _users.Values
            .OrderBy(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult<IReadOnlyList<UserEntity>>(users);
    }

    public Task<UserEntity> CreateAsync(string name, string email, CancellationToken ct = default)
    {
        var user = new UserEntity(Guid.NewGuid(), name, email, DateTimeOffset.UtcNow);
        _users.TryAdd(user.Id, user);
        return Task.FromResult(user);
    }

    public Task<UserEntity?> UpdateAsync(Guid id, string name, string email, CancellationToken ct = default)
    {
        if (!_users.TryGetValue(id, out var existing))
            return Task.FromResult<UserEntity?>(null);

        var updated = existing with { Name = name, Email = email };
        _users.TryUpdate(id, updated, existing);
        return Task.FromResult<UserEntity?>(updated);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        return Task.FromResult(_users.TryRemove(id, out _));
    }
}