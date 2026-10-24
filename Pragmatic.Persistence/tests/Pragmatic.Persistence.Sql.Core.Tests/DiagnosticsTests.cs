using System.Diagnostics;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The generator's diagnostics, read off a real build of the fixture project (every call site in it is outside
///     the subset): the codes, their lines, the census mode, and the EXPLAIN target against the test databases. A build
///     instead of a Roslyn test host: no extra package, and what is tested is what the developer sees.
/// </summary>
[Collection("databases")]
public class DiagnosticsTests(TestDatabases databases)
{
    private static readonly string Fixture = TestPaths.Fixture("Pragmatic.Persistence.Sql.DiagnosticsFixture", "Pragmatic.Persistence.Sql.DiagnosticsFixture.csproj");

    // The Explain tool as the tests' own build made it: copied beside the tests (a ProjectReference of this project), in their configuration,
    // wherever the build puts its output. Left to the target's default, the path names a Release build under the fixture nobody made.
    private static readonly string ExplainTool = Path.Combine(AppContext.BaseDirectory, "Pragmatic.Persistence.Sql.Explain.dll");

    [Fact]
    public void An_explain_build_reports_the_planners_findings_at_the_call_site()
    {
        if (databases.DockerUnavailable is not null)
            return;
        Build("-c Release"); // the generated explain file on disk (the fixture never compiles: the target is run alone, on that file)

        // PostgreSQL (EXPLAIN GENERIC_PLAN): the WHERE on Total, which has no index, is a Seq Scan with a filter at its call site; the one on Id is not.
        var (_, postgres) = Run($"msbuild \"{Fixture}\" -t:PragmaticExplain -p:Configuration=Release \"-p:PragmaticExplainTool={ExplainTool}\" --nologo -v q -nodeReuse:false", databases.ConnectionString(SqlDialect.PostgreSql));
        Assert.Contains("warning PRAG3017", postgres);
        Assert.Contains("Outside.cs(121,", postgres); // var scan = … Where(o => o.Total > 100m)
        Assert.Contains("EXPLAIN on postgresql: Seq Scan on Orders with filter", postgres);
        Assert.Contains("rows): no index serves the WHERE", postgres);
        Assert.DoesNotContain("Outside.cs(117,", postgres); // var fine = … Where((o, id) => o.Id == id): the key's index (the tool's summary line is below the quiet verbosity)
        // A hand-written statement with a bare identifier the model check does not read: the database refuses it, at the call site.
        Assert.Contains("warning PRAG3020", postgres);
        Assert.Contains("column \"totl\" does not exist", postgres);

        // SQLite (EXPLAIN QUERY PLAN): the same scan, in SQLite's words.
        var (_, sqlite) = Run($"msbuild \"{Fixture}\" -t:PragmaticExplain -p:Configuration=Release \"-p:PragmaticExplainTool={ExplainTool}\" --nologo -v q -nodeReuse:false", databases.ConnectionString(SqlDialect.Sqlite));
        Assert.Contains("warning PRAG3017", sqlite);
        Assert.Contains("Outside.cs(121,", sqlite);
        Assert.Contains("EXPLAIN on sqlite: SCAN t: the WHERE reads the whole table", sqlite);
        Assert.DoesNotContain("Outside.cs(117,", sqlite);
    }

    [Fact]
    public void Every_call_site_outside_the_subset_is_a_compile_time_diagnostic_with_its_remedy()
    {
        var (exitCode, output) = Build("-c Release");
        Assert.NotEqual(0, exitCode); // the fixture must not compile
        Assert.Contains("error PRAG3001", output);
        Assert.Contains("after a Select only Skip, Take, Distinct, the set operations and the terminal operators", output);
        Assert.Contains("error PRAG3002", output);
        Assert.Contains("'is Order' has no discriminator to test", output); // a type test outside a hierarchy in one table
        Assert.Contains("OfType<T>() narrows the root of a hierarchy in one table to one of its derived types (Charge is not a derived type of Charge)", output);
        Assert.Contains("ForUpdate locks the rows a statement returns: not an aggregate", output);
        Assert.Contains("Sql.RowNumber is a window function: a value of the select list or an ordering, not a predicate", output);
        Assert.Contains("Sql.SumOver's count must be a non-negative constant", output);
        Assert.Contains("CartLine feeds [RollUp] aggregates (Cart.Units, Cart.LineCount, Cart.Amount): a set-based command would leave them stale", output);
        Assert.Contains("Archive is [SoftDelete]: a set-based delete would remove the rows; mark them with ExecuteUpdateAsync, or purge with WithoutFilters()", output);
        Assert.Contains("Order is not [SoftDelete]: IncludeDeleted() reads its deleted rows", output);
        Assert.Contains("'query syntax' is not in the translated subset: a query expression binds to the operators with no invocation the generator can intercept", output);
        Assert.Contains("string stamped by the writer: it is string?", output); // [Auditable]'s CreatedBy: the user may be unknown
        Assert.Contains("CustomerSummary is read-only ([Table(ReadOnly = true)], a view): it is read, never written", output);
        Assert.Contains("Payment is the root of a table-per-concrete-type hierarchy, a view over the types' tables: update or delete through a derived type", output);
        Assert.Contains("the rows of CardPayment are all of that type (a table per concrete type): 'is CardPayment' is a test on the root's rows", output);
        Assert.Contains("'Count' is not a navigation or a collection of Order", output);
        Assert.Contains("'Orders' is taken per parent without an order: Take follows OrderBy in an Include", output);
        Assert.Contains("PRAG3015", output); // the fixture commits a census that no longer matches its queries
        Assert.Contains("queries.census.json does not match the queries this build translated", output);
        Assert.Contains("Take in an Include takes a positive constant", output);
        Assert.Contains("'id' is a value from outside the lambda", output); // a captured local: no tree carries it, the argument form does
        Assert.Contains("Where((o, arg) =>", output); // the remedy's ellipsis is not ASCII and the console's code page may rewrite it: asserted around it
        Assert.Contains(", id), with several values as a tuple", output);
        Assert.Contains("'since' in the Include filter is a value of the caller", output); // a filtered include takes columns and constants only
        Assert.Contains("AsAsyncEnumerable streams the rows one at a time", output); // no collection include when streaming
        Assert.Contains("a DTO with a collection reads the parent's key", output); // a declared projection with a List<TChildDto> needs the key to group on
        Assert.Contains("After continues an ordering", output); // a cursor without OrderBy
        Assert.Contains("the cursor has 1 value", output); // a cursor whose shape is not the keys'
        Assert.Contains("PRAG3009", output); // an ordering that is not total: a warning (an error here, warnings being errors)
        Assert.Contains("'Number' is not the key of Order", output);
        Assert.Contains("PRAG3010", output); // a query per row of another query's result: N+1, a warning
        Assert.Contains("'ToListAsync' runs once per 'customer' (Customer)", output);
        Assert.Contains("the date part must be a constant", output); // a catalog function's DatePart from a variable
        Assert.Contains("error PRAG3013", output); // a [SqlFunction] without its definition
        Assert.Contains("'NoDefinition' cannot be generated: no definition", output);
        Assert.Contains("a member of a set operation has no ordering, paging or cursor", output); // an OrderBy before a Union
        Assert.Contains("the members of a set operation project the same members in the same order", output); // two Selects that differ
        Assert.Contains("the inner chain of a join filters its rows (Where, WithoutFilters, WithoutFilter<TRule>, IncludeDeleted, a declared chain of those) and nothing else ('OrderBy' found)", output);
        Assert.Contains("a LEFT JOIN reads the inner row's columns as null: cast the member to its nullable type ((decimal?)o.Total)", output);
        Assert.Contains("Include and Project work on the entity's rows: put them before the join", output); // an Include on a joined row
        Assert.Contains("'OpenAndBig' cannot be generated: a declared chain does not call another declared chain", output);
        Assert.Contains("'minimum' is a value from outside the lambda", output); // a capture inside a subquery's lambda
        Assert.Contains("'OrderBy' is not translated inside a subquery", output);
        Assert.Contains("a GroupJoin is followed by a SelectMany over its group", output); // the group left unread at the terminal
        Assert.Contains("a Take in a correlated chain needs an OrderBy", output);
        Assert.Contains("'OrderBy' is not an aggregate step of a group", output);
        Assert.Contains("'NotAChain' cannot be generated: a declared chain's body is one expression: the chain on the first parameter", output);
        Assert.Contains("after a Select only Skip, Take, Distinct, the set operations and the terminal operators are translated", output); // a Where after the Select that closed the joined row
        Assert.Contains("TwoDialects has no rendering for SqlServer", output); // a definition whose switch throws for a dialect: not a fragment, so no arm there
        Assert.Contains("error CS0311", output); // Then on a string: the ISqlEntity constraint keeps it off non-entities (the string overload wins over IEnumerable<char>, and fails its constraint)
        Assert.Contains("error PRAG3003", output);
        Assert.Contains("'Link'", output);
        Assert.Contains("error PRAG3004", output);
        Assert.Contains("'WithoutKey.Customer'", output);
        // PRAG3005 (a chain typed IQueryable<T>, translated at runtime) is Info: the IDE shows it, the command-line compiler does not.
        Assert.Contains("error PRAG3006", output);
        Assert.Contains("the class must be partial", output);
        Assert.Contains("error PRAG3007", output);
        Assert.Contains("'IdentityOnComposite' has a composite key (Serial, Region)", output);
        // A hand-written statement against the model: the column the entity no longer has, the columns the statement does not return.
        Assert.Contains("error PRAG3026", output);
        Assert.Contains("\"Totl\" is not a column of Orders (its columns: Id, CustomerId, Number, Status, Total, CreatedAt, IsDeleted)", output);
        Assert.Contains("the statement does not return \"CustomerId\", \"Status\", \"Total\", \"CreatedAt\", \"IsDeleted\", which Order reads", output);
    }

    [Fact]
    public void A_census_build_prints_every_translated_call_site_with_its_sql()
    {
        // The spike promotes warnings to errors (Directory.Build.props), so the census lines come out as errors here; the code and the SQL are what matter.
        var (_, output) = Build("-c Release -p:PragmaticCensus=true");
        // The arrow in the message is not ASCII and the console's code page may rewrite it: the code and the two sides are asserted apart.
        Assert.Contains("PRAG3099: Where", output);
        Assert.Contains("t.\"Id\" = {0}", output);
        Assert.Contains("PRAG3099: ToListAsync", output);
        Assert.Contains("SELECT t.\"Id\", t.\"CustomerId\"", output); // the SQL of the translated chain, in the build log
        // The command terminals: the SET values are parameters, an expression over the row is translated, the WHERE is the chain's.
        Assert.Contains("PRAG3099: ExecuteUpdateAsync", output);
        Assert.Contains("UPDATE \"Events\" AS t SET \"Kind\" = @p1, \"Version\" = t.\"Version\" + 1 WHERE (t.\"OrderId\" = @p0)", output);
        Assert.Contains("PRAG3099: ExecuteDeleteAsync", output);
        Assert.Contains("DELETE FROM \"Events\" AS t WHERE (t.\"OrderId\" = @p0)", output);
        // The index analysis is information (hidden on the command line); the census promotes it too: a WHERE on Total, which has no index.
        // The indexes come from the workload's schema snapshot (schema/workload.schema.json among the fixture's AdditionalFiles), not from an attribute.
        Assert.Contains("PRAG3011", output);
        Assert.Contains("'Total' of Order has no index", output);
        Assert.DoesNotContain("'Id' of Order has no index", output); // the key is indexed by itself
        Assert.DoesNotContain("'CustomerId' of Order has no index", output); // IX_Orders_CustomerId in the snapshot; the entity carries no [Indexed]
        Assert.Contains("'OrderId' of Event has no index", output); // Events has no index on OrderId in the snapshot: reported
        Assert.Contains("PRAG3016", output); // the fixture's second snapshot is not one (a table without a name): reported, skipped
        Assert.Contains("'broken.schema.json' is not a schema snapshot (a table without a 'name')", output);
        // A catalog function emulated on a dialect: information, promoted by the census too.
        Assert.Contains("PRAG3012", output);
        Assert.Contains("Sql.Year on Sqlite is an emulation", output);
        // The static analysis of step 30 (information, promoted by the census): one case each in the fixture.
        Assert.Contains("PRAG3021", output);
        Assert.Contains("'ToListAsync' reads every row of Order the chain matches", output);
        Assert.DoesNotContain("reads every row of LookupCountry", output); // a [Lookup] table is read whole on purpose
        Assert.Contains("PRAG3022", output);
        Assert.Contains("'Number' of Order has an index, but 'Contains' is a LIKE that starts with a wildcard", output);
        Assert.Contains("PRAG3023", output);
        Assert.Contains("The predicate 'o => 1 == 1' does not read the row", output);
        Assert.Contains("PRAG3024", output);
        Assert.Contains("Include(Customer) loads the whole Customer row with every Order, and this method reads only its 'Name'", output);
        Assert.Contains("PRAG3025", output);
        Assert.Contains("'Outside.Run' runs 4 writes with no transaction around them", output); // the two refused command chains count as writes too: they are written as such
    }

    private static (int ExitCode, string Output) Build(string arguments) => Run($"build \"{Fixture}\" {arguments} --nologo -v q -nodeReuse:false", null);

    /// <summary>A dotnet command on the fixture; <paramref name="explain" /> is the connection string the PragmaticExplain target reads (as an environment variable: no semicolons to escape on the command line).</summary>
    private static (int ExitCode, string Output) Run(string arguments, string? explain)
    {
        var start = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (explain is not null)
            start.Environment["PragmaticExplain"] = explain;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
