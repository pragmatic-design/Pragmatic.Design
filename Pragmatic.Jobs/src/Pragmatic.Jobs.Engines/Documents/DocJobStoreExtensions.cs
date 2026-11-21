using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Persistence.Documents;
using Pragmatic.Temporal.Clock;

namespace Pragmatic.Jobs.Engines.Documents;

/// <summary>
///     The durable job store on the documents engine, in two halves as on EF Core: the <c>[EnableJobPersistence]</c> boundary's generated
///     <c>Add{Boundary}Persistence</c> hands its session to the store (<see cref="AddDocJobStore{TBoundary}" />), and the host chooses the store
///     (<see cref="UseDocumentsPersistence" />). The collections are in the boundary's <c>{Boundary}DocSchema</c>
///     (<see cref="JobDocument.Schema" />, <see cref="RecurringJobDocument.Schema" />).
/// </summary>
public static class DocJobStoreExtensions
{
    /// <summary>The session of <typeparamref name="TBoundary" />, the <c>[EnableJobPersistence]</c> boundary, as the job store's.</summary>
    public static IServiceCollection AddDocJobStore<TBoundary>(this IServiceCollection services)
    {
        services.TryAddScoped(static sp => new DocJobStoreSession(sp.GetRequiredKeyedService<DocSession>(typeof(TBoundary))));
        return services;
    }

    /// <summary>
    ///     Replaces the in-memory stores with the documents engine's, over the session of the <c>[EnableJobPersistence]</c> boundary. The order
    ///     against <c>AddPragmaticJobs()</c> does not matter: the stores already registered are dropped, and the in-memory defaults are added
    ///     only where none is.
    /// </summary>
    public static JobsBuilder UseDocumentsPersistence(this JobsBuilder builder)
    {
        builder.Services.RemoveAll<IJobStore>();
        builder.Services.RemoveAll<IRecurringJobStore>();
        builder.Services.AddScoped<IJobStore>(static sp => new DocJobStore(Session(sp), sp.GetRequiredService<IClock>(), sp.GetRequiredService<ILogger<DocJobStore>>()));
        builder.Services.AddScoped<IRecurringJobStore>(static sp => new DocRecurringJobStore(Session(sp)));
        return builder;
    }

    private static DocSession Session(IServiceProvider services)
        => (services.GetService<DocJobStoreSession>() ?? throw new InvalidOperationException(
            "UseDocumentsPersistence() keeps the jobs in the database of the boundary marked [EnableJobPersistence], and no boundary the documents "
            + "engine serves is: mark the one whose database should hold __Jobs and __RecurringJobs, so its generated registration hands its "
            + "session to the store.")).Session;
}
