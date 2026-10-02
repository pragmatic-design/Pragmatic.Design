using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.ConnectionString;

/// <summary>
///     Provides connection strings dynamically for a DbContext.
///     Use this for multi-tenant scenarios or dynamic database selection.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type this provider is for.</typeparam>
public interface IConnectionStringProvider<TDbContext> where TDbContext : DbContext
{
    /// <summary>
    ///     Gets the connection string for the current context (tenant, request, etc.).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The connection string to use.</returns>
    ValueTask<string> GetConnectionStringAsync(CancellationToken ct = default);
}