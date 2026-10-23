using System.Text.Json;
using Pragmatic.Persistence.Sql.Explain;

// dotnet Pragmatic.Persistence.Sql.Explain.dll --census <queries.explain.g.cs> --connection <connection string> [--dialect sqlite|postgresql|sqlserver]
//
// Reads the queries the generator translated (the explain file: one object per call site with its SQL per dialect), runs EXPLAIN for
// each statement on the database named, and prints what the planner says costs more than it should, in MSBuild's canonical format
// (path(line,column): warning PRAGCnnn: message), so the build reports them at the call site:
//   PRAG3017 a sequential scan with a filter (the WHERE reads every row), PRAG3018 a sort the planner runs (no index serves the
//   ordering), PRAG3019 a nested loop over many rows, PRAG3020 a statement EXPLAIN could not run (the reason).
// Exit code 0 whatever the findings: they are warnings; 2 when the arguments are wrong.

string? census = null, connection = null, dialect = null;
var verbose = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--census" when i + 1 < args.Length: census = args[++i]; break;
        case "--connection" when i + 1 < args.Length: connection = args[++i]; break;
        case "--dialect" when i + 1 < args.Length: dialect = args[++i]; break;
        case "--verbose": verbose = true; break; // every statement and its plan as the database printed it
        default:
            Console.Error.WriteLine($"unknown option {args[i]}");
            return 2;
    }
}

if (census is null || connection is null)
{
    Console.Error.WriteLine("usage: --census <queries.explain.g.cs> --connection <connection string> [--dialect sqlite|postgresql|sqlserver]");
    return 2;
}

var entries = ExplainFile.Read(census);
dialect ??= Explainer.Detect(connection);
Explainer explainer;
try
{
    explainer = Explainer.For(dialect, connection);
}
catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
{
    Console.WriteLine($"{census}(1,1): warning PRAG3020: EXPLAIN skipped on {dialect}: {e.Message}");
    return 0;
}

explainer.Verbose = verbose;
using (explainer)
{
    try
    {
        await explainer.OpenAsync();
    }
    catch (Exception e) when (e is not OutOfMemoryException)
    {
        Console.WriteLine($"{census}(1,1): warning PRAG3020: EXPLAIN skipped on {dialect}: the database did not answer ({e.GetType().Name}: {e.Message.Split('\n')[0].Trim()})");
        return 0;
    }

    var statements = 0;
    var findings = 0;
    foreach (var entry in entries)
    {
        var sql = entry.Sql(dialect);
        if (sql is null)
            continue;
        foreach (var statement in Explainer.Statements(sql))
        {
            statements++;
            foreach (var finding in await explainer.ExplainAsync(statement))
            {
                findings++;
                Console.WriteLine($"{entry.Path}({entry.Line},{entry.Column}): warning {finding.Code}: EXPLAIN on {dialect}: {finding.Message} [{entry.Kind} in {entry.Member}: {Explainer.Shorten(statement)}]");
            }
        }
    }

    Console.WriteLine($"PragmaticExplain: {statements} statement(s) explained on {dialect}, {findings} finding(s).");
}

return 0;

namespace Pragmatic.Persistence.Sql.Explain
{
    /// <summary>One translated query with its call site and its SQL per dialect, as the generator wrote it into queries.explain.g.cs.</summary>
    internal sealed record ExplainEntry(string Path, int Line, int Column, string Member, string Kind, string Sqlite, string PostgreSql, string SqlServer)
    {
        public string? Sql(string dialect) => dialect switch
        {
            "sqlite" => Sqlite,
            "postgresql" => PostgreSql,
            "sqlserver" => SqlServer,
            _ => null,
        };
    }

    internal static class ExplainFile
    {
        /// <summary>The JSON between the generated file's #if / #endif, parsed: an array with one object per line, so its lines are the ones starting with [, { or ].</summary>
        public static List<ExplainEntry> Read(string path)
        {
            var lines = File.ReadAllLines(path).Where(l => l.StartsWith('[') || l.StartsWith('{') || l.StartsWith(']'));
            using var document = JsonDocument.Parse(string.Join("\n", lines));
            var entries = new List<ExplainEntry>();
            foreach (var e in document.RootElement.EnumerateArray())
                entries.Add(new ExplainEntry(e.GetProperty("path").GetString()!, e.GetProperty("line").GetInt32(), e.GetProperty("column").GetInt32(), e.GetProperty("member").GetString()!, e.GetProperty("kind").GetString()!, e.GetProperty("sqlite").GetString()!, e.GetProperty("postgresql").GetString()!, e.GetProperty("sqlserver").GetString()!));
            return entries;
        }
    }
}
