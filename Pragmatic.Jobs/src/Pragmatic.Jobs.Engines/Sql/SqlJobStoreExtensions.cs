using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Persistence.Sql;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Engines.Sql;

/// <summary>
///     The durable job store on the Sql engine, in two halves as on EF Core: the <c>[EnableJobPersistence]</c> boundary's generated
///     <c>Add{Boundary}Persistence</c> hands its session to the store (<see cref="AddSqlJobStore{TBoundary}" />), and the host chooses the store
///     (<see cref="UseSqlPersistence" />), because which store runs is a deployment's decision. The tables are in the boundary's database's
///     schema, with its own.
/// </summary>
public static class SqlJobStoreExtensions
{
    /// <summary>The session of <typeparamref name="TBoundary" />, the <c>[EnableJobPersistence]</c> boundary, as the job store's.</summary>
    public static IServiceCollection AddSqlJobStore<TBoundary>(this IServiceCollection services)
    {
        services.TryAddScoped(static sp => new SqlJobStoreSession(sp.GetRequiredKeyedService<SqlSession>(typeof(TBoundary))));
        return services;
    }

    /// <summary>
    ///     Replaces the in-memory stores with the Sql engine's, over the session of the <c>[EnableJobPersistence]</c> boundary. The order
    ///     against <c>AddPragmaticJobs()</c> does not matter: the stores already registered are dropped, and the in-memory defaults are added
    ///     only where none is.
    /// </summary>
    public static JobsBuilder UseSqlPersistence(this JobsBuilder builder)
    {
        builder.Services.RemoveAll<IJobStore>();
        builder.Services.RemoveAll<IRecurringJobStore>();
        builder.Services.AddScoped<IJobStore>(static sp => new SqlJobStore(Session(sp), sp.GetRequiredService<IClock>(), sp.GetRequiredService<ILogger<SqlJobStore>>()));
        builder.Services.AddScoped<IRecurringJobStore>(static sp => new SqlRecurringJobStore(Session(sp)));
        return builder;
    }

    private static SqlSession Session(IServiceProvider services)
        => (services.GetService<SqlJobStoreSession>() ?? throw new InvalidOperationException(
            "UseSqlPersistence() keeps the jobs in the database of the boundary marked [EnableJobPersistence], and no boundary the Sql engine "
            + "serves is: mark the one whose database should hold __Jobs and __RecurringJobs, so its generated registration hands its session "
            + "to the store.")).Session;
}
