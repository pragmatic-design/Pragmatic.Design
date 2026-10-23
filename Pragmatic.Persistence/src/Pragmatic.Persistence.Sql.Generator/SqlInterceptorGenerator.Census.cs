using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Sql.Generator.Templates;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>One translated query of the census: where it is written, what it is, and its SQL per dialect.</summary>
internal sealed class CensusEntry
{
    public CensusEntry(string file, string member, string kind, string chain, string sqlite, string postgreSql, string sqlServer, string mySql, string oracle, string path, int line, int column)
    {
        Oracle = oracle;
        File = file;
        Member = member;
        Kind = kind;
        Chain = chain;
        Sqlite = sqlite;
        PostgreSql = postgreSql;
        SqlServer = sqlServer;
        MySql = mySql;
        Path = path;
        Line = line;
        Column = column;
    }

    public string File { get; }

    /// <summary>The call site's full path and 1-based position: for the explain file (a build artifact), never for the committed census.</summary>
    public string Path { get; }

    public int Line { get; }

    public int Column { get; }

    public string Member { get; }

    public string Kind { get; }

    public string Chain { get; }

    public string Sqlite { get; }

    public string PostgreSql { get; }

    public string SqlServer { get; }

    public string MySql { get; }

    public string Oracle { get; }
}

/// <summary>
///     The census as a file: every translated call site and <c>[Query]</c> with its SQL per dialect, as JSON in a generated source
///     (<c>queries.census.g.cs</c>, the JSON inside a comment) that the <c>PragmaticCensus</c> target copies to the project's
///     <c>queries.census.json</c> after the build, to be committed: a PR that changes a query shows the SQL that changed in its diff.
///     When the project lists that file among its AdditionalFiles, the build compares it with the fresh census and reports PRAG3015
///     while they differ (a CI with warnings as errors refuses a stale census). Line numbers are left out: a query moved is not a query changed.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    public const string CensusFileName = "queries.census.json";

    /// <summary>The entry of a terminal call site, from its syntax (file, enclosing member, the chain as written) and its plan.</summary>
    private static CensusEntry CensusOf(CallSite call, string kind, PlanBuilder builder)
    {
        var span = call.Invocation.GetLocation().GetLineSpan();
        var file = System.IO.Path.GetFileName(span.Path);
        var member = call.Invocation.Ancestors().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>().FirstOrDefault(m => m is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax);
        var type = member?.Ancestors().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>().FirstOrDefault();
        var name = member switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method => $"{type?.Identifier.ValueText}.{method.Identifier.ValueText}",
            Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax property => $"{type?.Identifier.ValueText}.{property.Identifier.ValueText}",
            Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax constructor => $"{constructor.Identifier.ValueText}()",
            _ => "",
        };
        var chain = System.Text.RegularExpressions.Regex.Replace(call.Invocation.ToString(), @"\s+", " ");
        return new CensusEntry(file, name, kind, chain, builder.Sql(SqlDialect.Sqlite), builder.Sql(SqlDialect.PostgreSql), builder.Sql(SqlDialect.SqlServer), builder.Sql(SqlDialect.MySql), builder.Sql(SqlDialect.Oracle), span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
    }

    /// <summary>The census source and, when the project committed a census, the comparison with it.</summary>
    private static void EmitCensus(SourceProductionContext spc, List<CensusEntry> census, string? committed, List<Diagnostic> diagnostics)
    {
        // The JSON sits in a disabled #if block (never compiled); the target copies the lines between the two directives (no wildcard characters, which MSBuild item removal would read as globs).
        var json = CensusJson(census);
        spc.AddSource(new QueryListTemplate("queries.census.g.cs", "PRAGMATIC_CENSUS_FILE",
            "the queries this build translated, with their SQL per dialect; the PragmaticCensus target copies the JSON below to queries.census.json", json).RenderOutput());
        if (committed is not null && Normalize(committed) != Normalize(json))
            diagnostics.Add(Diagnostic.Create(Diagnostics.CensusStale, Location.None, CensusFileName, $"{census.Count} queries"));

        // The same queries with their call sites (full path, line, column), for the PragmaticExplain target's tool: a build artifact, never committed (a moved query would change it).
        spc.AddSource(new QueryListTemplate("queries.explain.g.cs", "PRAGMATIC_EXPLAIN_FILE",
            "the queries this build translated with their call sites, for the PragmaticExplain target", ExplainJson(census)).RenderOutput());
    }

    /// <summary>The census entries with their positions, one object per line, in the census's order.</summary>
    internal static string ExplainJson(List<CensusEntry> census)
        => JsonLines(census.OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Line).ThenBy(e => e.Column)
            .Select(e => $"{{ \"path\": {Json(e.Path)}, \"line\": {e.Line}, \"column\": {e.Column}, \"member\": {Json(e.Member)}, \"kind\": {Json(e.Kind)}, {Dialects(e)} }}"));

    /// <summary>The SQL of an entry per dialect, as the JSON members both lists end with.</summary>
    private static string Dialects(CensusEntry e)
        => $"\"sqlite\": {Json(e.Sqlite)}, \"postgresql\": {Json(e.PostgreSql)}, \"sqlserver\": {Json(e.SqlServer)}, \"mysql\": {Json(e.MySql)}, \"oracle\": {Json(e.Oracle)}";

    /// <summary>A JSON array of the objects, one per line.</summary>
    private static string JsonLines(IEnumerable<string> objects)
    {
        var lines = objects.ToList();
        return "[\n" + string.Join(",\n", lines) + (lines.Count > 0 ? "\n" : "") + "]\n";
    }

    /// <summary>Line endings and the lines' edges aside (MSBuild trims the lines it copies): the content is what is compared.</summary>
    private static string Normalize(string text) => string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    /// <summary>The entries sorted by file, member and chain, one object per line: a stable text whose diff is the queries' diff.</summary>
    internal static string CensusJson(List<CensusEntry> census)
        => JsonLines(census.OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.Member, StringComparer.Ordinal).ThenBy(e => e.Chain, StringComparer.Ordinal)
            .Select(e => $"{{ \"file\": {Json(e.File)}, \"member\": {Json(e.Member)}, \"kind\": {Json(e.Kind)}, \"chain\": {Json(e.Chain)}, {Dialects(e)} }}"));

    /// <summary>A JSON string literal: the quote, the backslash and the control characters escaped.</summary>
    private static string Json(string text)
        => "\"" + string.Concat(text.Select(c => c switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            < ' ' => "\\u" + ((int)c).ToString("x4"),
            _ => c.ToString(),
        })) + "\"";
}
