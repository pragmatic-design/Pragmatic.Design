using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Executors;

namespace Pragmatic.Persistence.EFCore.Extensions;

/// <summary>
///     Convenience DI registration for library / sample apps that aren't running inside a
///     <c>PragmaticHost</c>. In host mode the unified SG composes this wiring from
///     per-assembly metadata; here we expose the same pieces as plain extension methods so
///     a console app or minimal API can register persistence without the full composition.
/// </summary>
public static class PragmaticPersistenceEFCoreExtensions
{
    /// <summary>
    ///     Registers the EF Core persistence runtime:
    ///     <list type="bullet">
    ///         <item><description><c>IQueryExecutor</c> → <c>EfCoreQueryExecutor</c> (scoped)</description></item>
    ///         <item><description><c>DbContext</c> base → <typeparamref name="TDbContext"/> (scoped) so the SG-generated [Query]+[Endpoint] handler can resolve it</description></item>
    ///     </list>
    ///     Pair with the SG-generated <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c>
    ///     (emitted per assembly when <c>[Entity]</c> types are present) to also wire up
    ///     the individual <c>IRepository&lt;T&gt;</c> mappings.
    /// </summary>
    /// <typeparam name="TDbContext">The concrete DbContext registered via <c>AddDbContext</c>.</typeparam>
    public static IServiceCollection AddPragmaticPersistenceEFCore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        // The fluent builder's AsNoTracking()/AsSplitQuery() are EF Core concepts, and the assembly
        // that declares them does not reference EF Core. This is where the two meet.
        Query.EfQueryHintApplier.Install();

        services.TryAddScoped<IQueryExecutor, EfCoreQueryExecutor>();
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TDbContext>());
        return services;
    }
}
