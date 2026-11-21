using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Engines.Documents;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Temporal.Clock;
using Showcase.Booking;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     The durable job store the job suites run against, on the documents engine (#904): <see cref="DocJobStore" /> and
///     <see cref="DocRecurringJobStore" /> over the Booking boundary's session, which is [EnableJobPersistence] and keeps the <c>__Jobs</c> and
///     <c>__RecurringJobs</c> collections in the App database, created by the host's documents schema at start. In place of the EF Core suite's
///     file, which runs EF Core's store over a DbContext of its own.
/// </summary>
public sealed class JobStoreUnderTest(PostgresFixture fixture)
{
    /// <summary>A pair of stores over a session of their own, from a scope of the running host: a second call is a second worker.</summary>
    public Task<JobStores> OpenAsync()
    {
        var scope = fixture.SharedHost.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredKeyedService<DocSession>(typeof(BookingBoundary));
        return Task.FromResult(new JobStores(new DocJobStore(session, SystemClock.Instance, NullLogger<DocJobStore>.Instance), new DocRecurringJobStore(session), scope));
    }

    /// <summary>
    ///     What the store reads through, for a host of the test's own: the Booking boundary's session over the App database, handed to the store
    ///     as the boundary's generated registration hands it. The running host is started first, so the collections and their indexes are there.
    /// </summary>
    public Task AddToAsync(IServiceCollection services)
    {
        _ = fixture.SharedHost.Services;
        var settings = MongoSettings.Parse(fixture.AppConnectionString);
        services.AddSingleton(_ => new MongoClient(settings));
        services.AddKeyedScoped<DocSession>(typeof(BookingBoundary), (sp, _) => new DocSession(sp.GetRequiredService<MongoClient>(),
            settings.Database ?? throw new InvalidOperationException("The App connection string names no database in its path.")));
        services.AddDocJobStore<BookingBoundary>();
        return Task.CompletedTask;
    }

    /// <summary>The host's switch to this store.</summary>
    public static void Use(JobsBuilder jobs) => jobs.UseDocumentsPersistence();

    /// <summary>The real generated registration of Booking's jobs, in this engine's copy of Booking (its namespace is the copy's).</summary>
    public static void AddBookingJobs(IServiceCollection services) => Showcase.Booking.Documents.PragmaticJobRegistration.AddDiscoveredJobs(services);

    /// <summary>Every job the store holds: what a test reads to find the ones a run wrote under its correlation.</summary>
    /// <remarks>
    ///     The keys are read with a find command of the test's own, and each job through the store: a chain on the engine is translated where it
    ///     is written, and the generator does not run on this test assembly. The collection of a test run fits one batch.
    /// </remarks>
    public async Task<IReadOnlyList<JobInstance>> AllJobsAsync()
    {
        var settings = MongoSettings.Parse(fixture.AppConnectionString);
        var ids = new List<Guid>();
        await using (var client = new MongoClient(settings))
        {
            var reply = await client.RunCommandAsync(settings.Database!, static w =>
            {
                w.Write("find", "__Jobs");
                w.Write("batchSize", 100_000);
            });
            if (reply.TryGetDocument("cursor", out var cursor) && cursor.TryGetDocument("firstBatch", out var batch))
            {
                foreach (var document in batch.Documents())
                {
                    if (document.TryGetBinary("_id", out var id))
                        ids.Add(new Guid(id.Span, bigEndian: true));
                }
            }
        }

        await using var stores = await OpenAsync();
        var jobs = new List<JobInstance>(ids.Count);
        foreach (var id in ids)
        {
            if (await stores.Jobs.GetAsync(id) is { } job)
                jobs.Add(job);
        }

        return jobs;
    }
}
