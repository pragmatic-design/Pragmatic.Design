using System.Data.Common;
using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>Runs every area on one session; the Extras tables (the model samples) are created on the way.</summary>
internal static class Samples
{
    public static async Task<int> RunAllAsync(SqlSession session, Seed seed, Func<DbConnection> connect)
    {
        await ExtrasSchema.CreateAsync(session.Connection, session.Dialect);
        var failures = 0;
        failures += await Reads.RunAsync(session, seed);
        failures += await Algebra.RunAsync(session, seed);
        failures += await Includes.RunAsync(session, seed);
        failures += await Writes.RunAsync(session, seed);
        failures += await Declared.RunAsync(session, seed);
        failures += await Runtime.RunAsync(session, seed);
        failures += await Model.RunAsync(session, seed);
        failures += await Sessions.RunAsync(session, seed, connect);
        return failures;
    }
}

/// <summary>What a sample checks: one line per check, the area and the dialect in front, FAIL where the seed disagrees.</summary>
internal sealed class Checks(SqlSession session, string area)
{
    public int Failures { get; private set; }

    public void Check(string what, bool ok)
    {
        Console.WriteLine($"{session.Dialect} {area}: {what}: {(ok ? "ok" : "FAIL")}");
        if (!ok)
            Failures++;
    }
}
