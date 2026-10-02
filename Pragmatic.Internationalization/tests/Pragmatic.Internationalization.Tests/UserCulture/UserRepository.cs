using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Persistence.Repository;
using Pragmatic.Specification;

namespace Pragmatic.Internationalization.Tests.UserCulture;

/// <summary>
///     The seam the generated resolver reads through, over this suite's SQLite context.
/// </summary>
/// <remarks>
///     In an application this type is generated per entity by the persistence generator and registered
///     with it. This suite does not reference Pragmatic.Persistence — it is about the culture provider,
///     not about persistence — so it supplies the contract itself. Only <see cref="Query" /> is
///     exercised: it is the one member the resolver uses, because
///     <c>II18NConfigProvider.GetConfiguration()</c> is synchronous and needs a synchronous path.
/// </remarks>
internal sealed class UserRepository(UserDbContext context) : IReadRepository<AppUser>
{
    public IQueryable<AppUser> Query() => context.Users;

    public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<List<AppUser>> FindAsync(ISpecification<AppUser> spec, CancellationToken ct = default)
        => throw new NotSupportedException("The culture suite only reads through Query().");

    public Task<int> CountAsync(ISpecification<AppUser> spec, CancellationToken ct = default)
        => throw new NotSupportedException("The culture suite only reads through Query().");

    public Task<bool> ExistsAsync(ISpecification<AppUser> spec, CancellationToken ct = default)
        => throw new NotSupportedException("The culture suite only reads through Query().");

    public Task<AppUser?> FirstOrDefaultAsync(ISpecification<AppUser> spec, CancellationToken ct = default)
        => throw new NotSupportedException("The culture suite only reads through Query().");

    public Task<IReadOnlyList<TResult>> RunAsync<TResult>(
        IQuery<AppUser, TResult> query, CancellationToken ct = default) where TResult : class
        => throw new NotSupportedException("The culture suite only reads through Query().");

    public Task<PagedResult<TResult>> RunAsync<TResult>(
        IPagedQuery<AppUser, TResult> query, CancellationToken ct = default) where TResult : class
        => throw new NotSupportedException("The culture suite only reads through Query().");
}
