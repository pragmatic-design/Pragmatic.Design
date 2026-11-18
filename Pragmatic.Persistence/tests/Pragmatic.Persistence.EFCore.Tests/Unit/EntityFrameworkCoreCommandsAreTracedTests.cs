using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Pragmatic.Persistence.EFCore.Extensions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

/// <summary>
///     A host on EF Core still traces its commands now that the instrumentation is registered by Pragmatic.Persistence.EFCore rather
///     than by the composition host (#858).
/// </summary>
/// <remarks>
///     The tracer provider is built the way a host's telemetry builds it (<c>AddOpenTelemetry().WithTracing</c>), and a command runs on
///     SQLite. The control, without the registration, sees no activity from the instrumentation: the listener alone does not make one.
/// </remarks>
[Collection(nameof(EntityFrameworkCoreCommandsAreTracedTests))]
public sealed class EntityFrameworkCoreCommandsAreTracedTests
{
    private const string InstrumentationSource = "OpenTelemetry.Instrumentation.EntityFrameworkCore";

    [Fact]
    public void WithTheRegistration_ACommandIsTraced()
        => ActivitiesOfACommand(register: true).Should().NotBeEmpty("EF Core's instrumentation joins the host's tracer provider");

    [Fact]
    public void WithoutTheRegistration_NoCommandIsTraced()
        => ActivitiesOfACommand(register: false).Should().BeEmpty("nothing else subscribes EF Core's diagnostics");

    private static List<Activity> ActivitiesOfACommand(bool register)
    {
        var services = new ServiceCollection();
        services.AddOpenTelemetry().WithTracing(static _ => { });
        if (register)
            services.AddPragmaticEntityFrameworkCoreTracing();

        using var provider = services.BuildServiceProvider();
        using var tracer = provider.GetRequiredService<TracerProvider>();

        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name.StartsWith(InstrumentationSource, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (activities)
                    activities.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = new DbContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        db.Database.ExecuteSqlRaw("SELECT 1");

        lock (activities)
            return [.. activities];
    }
}
