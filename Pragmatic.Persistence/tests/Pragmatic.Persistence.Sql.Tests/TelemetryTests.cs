using System.Diagnostics;
using System.Diagnostics.Metrics;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Origin and metrics: every plan knows its call site (<c>File.cs(line,col) in Type.Method Plan_N</c>); a session that annotates
///     its statements writes it as a leading comment, which the database's log then shows; the meter counts and times executions per
///     plan and the activity source traces them, both tagged with the origin, and nothing runs while nobody listens.
/// </summary>
[Collection("databases")]
public class TelemetryTests(TestDatabases databases)
{
    [Fact]
    public async Task Executions_are_counted_timed_and_traced_per_plan_origin()
    {
        var measurements = new List<(string Instrument, double Value, string Plan, string System)>();
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SqlTelemetry.Name)
                listener.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        meter.Start();
        var activities = new List<Activity>();
        using var tracing = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SqlTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(tracing);

        using var session = databases.Session(SqlDialect.Sqlite);
        var customer = databases.Seed.BusyCustomer;
        for (var i = 0; i < 3; i++)
            _ = await session.Query<Order>().CountAsync((o, id) => o.CustomerId == id && !o.IsDeleted, customer.Id);
        _ = await session.Query<Customer>().Where((c, id) => c.Id == id, customer.Id).ToListAsync();

        // The counter: three executions of the count's plan, one of the list's, each tagged with its call site and the database.
        var counted = measurements.Where(m => m.Instrument == "pragmatic.sql.executions" && m.Plan.Contains("TelemetryTests.cs(", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, counted.Select(m => m.Plan).Distinct().Count());
        Assert.Equal(3, counted.Where(m => m.Plan.Contains("CountAsync", StringComparison.Ordinal) || counted.Count(c => c.Plan == m.Plan) == 3).Sum(m => (long)m.Value));
        Assert.All(counted, m => Assert.Equal("sqlite", m.System));
        Assert.All(counted, m => Assert.Contains(" in TelemetryTests.Executions_are_counted_timed_and_traced_per_plan_origin Plan_", m.Plan, StringComparison.Ordinal));
        // The histogram: one duration per execution, positive.
        var timed = measurements.Where(m => m.Instrument == "pragmatic.sql.duration" && m.Plan.Contains("TelemetryTests.cs(", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, timed.Count);
        Assert.All(timed, m => Assert.True(m.Value > 0));
        // The activities: one per execution, the same tags.
        var traced = activities.Where(a => a.GetTagItem("pragmatic.plan") is string plan && plan.Contains("TelemetryTests.cs(", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, traced.Count);
        Assert.All(traced, a => Assert.Equal("sqlite", a.GetTagItem("db.system")));
        Assert.All(traced, a => Assert.Equal(ActivityKind.Client, a.Kind));

        void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string plan = "", system = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "pragmatic.plan")
                    plan = (string)tag.Value!;
                else if (tag.Key == "db.system")
                    system = (string)tag.Value!;
            }

            lock (measurements)
                measurements.Add((instrument.Name, value, plan, system));
        }
    }

    [Fact]
    public async Task An_annotating_session_prefixes_the_statement_with_the_origin_and_the_database_log_shows_it()
    {
        using var plain = databases.Session(SqlDialect.Sqlite);
        using var annotating = new SqlSession(databases.Open(SqlDialect.Sqlite), SqlDialect.Sqlite) { AnnotateStatements = true };
        var customer = databases.Seed.BusyCustomer;
        using var command = annotating.Query<Order>().Where((o, id) => o.CustomerId == id, customer.Id).ToCommand();
        Assert.StartsWith("/* TelemetryTests.cs(", command.CommandText, StringComparison.Ordinal);
        Assert.Contains(" in TelemetryTests.An_annotating_session_prefixes_the_statement_with_the_origin_and_the_database_log_shows_it Plan_", command.CommandText, StringComparison.Ordinal);
        Assert.Contains("*/ SELECT ", command.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("/*", plain.Query<Order>().Where((o, id) => o.CustomerId == id, customer.Id).ToCommand().CommandText, StringComparison.Ordinal);
        // The annotated statement runs as the plain one does.
        Assert.Equal(await plain.Query<Order>().CountAsync((o, id) => o.CustomerId == id, customer.Id), await annotating.Query<Order>().CountAsync((o, id) => o.CustomerId == id, customer.Id));
        if (databases.DockerUnavailable is not null)
            return;

        // PostgreSQL logs every statement: the one this session ran carries its call site.
        using var postgres = new SqlSession(databases.Open(SqlDialect.PostgreSql), SqlDialect.PostgreSql) { AnnotateStatements = true };
        _ = await postgres.Query<Order>().CountAsync((o, id) => o.CustomerId == id && !o.IsDeleted, customer.Id);
        var log = await databases.PostgresLogAsync();
        Assert.Contains("/* TelemetryTests.cs(", log, StringComparison.Ordinal);
        Assert.Contains("An_annotating_session_prefixes_the_statement_with_the_origin_and_the_database_log_shows_it Plan_", log, StringComparison.Ordinal);
    }
}
