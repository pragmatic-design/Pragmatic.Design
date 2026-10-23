using System.Data.Common;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Explain;

/// <summary>What the planner said about one statement that costs more than it should: the code of the finding and its text.</summary>
internal sealed record Finding(string Code, string Message)
{
    public const string SequentialScan = "PRAG3017";
    public const string Sort = "PRAG3018";
    public const string WideNestedLoop = "PRAG3019";
    public const string NotExplained = "PRAG3020";
}

/// <summary>EXPLAIN of a statement on one dialect, its plan read for the patterns worth a warning at the call site.</summary>
internal abstract class Explainer : IDisposable
{
    private static readonly Regex Parameter = new(@"@p(\d+)", RegexOptions.Compiled);
    private static readonly Regex ListPlaceholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    protected abstract DbConnection Connection { get; }

    /// <summary>True to print every statement and the plan the database answered with, as it is.</summary>
    public bool Verbose { get; set; }

    public Task OpenAsync() => Connection.OpenAsync();

    protected void Trace(string statement, string plan)
    {
        if (!Verbose)
            return;
        Console.WriteLine("  statement: " + statement);
        Console.WriteLine("  plan: " + plan.Replace("\r", "").Replace("\n", "\n        "));
    }

    public abstract Task<IReadOnlyList<Finding>> ExplainAsync(string statement);

    public void Dispose() => Connection.Dispose();

    /// <summary>The dialect from the connection string's keys: Host= is Npgsql's, Initial Catalog / Server= / User ID SqlClient's, Data Source alone SQLite's.</summary>
    public static string Detect(string connectionString)
    {
        var text = connectionString.Replace(" ", "");
        if (text.Contains("Host=", StringComparison.OrdinalIgnoreCase))
            return "postgresql";
        if (text.Contains("InitialCatalog=", StringComparison.OrdinalIgnoreCase) || text.Contains("Server=", StringComparison.OrdinalIgnoreCase) || text.Contains("UserID=", StringComparison.OrdinalIgnoreCase) || text.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
            return "sqlserver";
        return "sqlite";
    }

    public static Explainer For(string dialect, string connectionString) => dialect switch
    {
        "sqlite" => new SqliteExplainer(connectionString),
        "postgresql" => new PostgresExplainer(connectionString),
        "sqlserver" => throw new NotSupportedException("SQL Server plans (SET SHOWPLAN_XML ON) are not read yet: run against PostgreSQL or SQLite"),
        _ => throw new NotSupportedException($"'{dialect}' is not a dialect (sqlite, postgresql, sqlserver)"),
    };

    /// <summary>A plan's statements: the rows' own and its companions (collection includes), joined by "; " in the generated text.</summary>
    public static IEnumerable<string> Statements(string sql) => sql.Split("; ", StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);

    /// <summary>The statement with PostgreSQL's positional parameters (@pN and the IN-list {N}, one array parameter there, both become $N+1), and how many it takes (the highest N + 1).</summary>
    protected static string Positional(string statement, out int parameters)
    {
        var highest = -1;
        string Dollar(Match m)
        {
            var n = int.Parse(m.Groups[1].Value);
            highest = Math.Max(highest, n);
            return "$" + (n + 1);
        }

        var text = ListPlaceholder.Replace(Parameter.Replace(statement, Dollar), Dollar);
        parameters = highest + 1;
        return text;
    }

    /// <summary>The generated IN-list placeholders ({N}, expanded per call at runtime) as NULL, so the statement parses; named parameters stay unbound.</summary>
    protected static string WithoutLists(string statement) => ListPlaceholder.Replace(statement, "NULL");

    public static string Shorten(string statement) => statement.Length <= 160 ? statement : statement.Substring(0, 157) + "...";

    protected static Finding NotExplained(Exception e) => new(Finding.NotExplained, $"the statement could not be explained ({e.Message.Split('\n')[0].Trim()})");
}
