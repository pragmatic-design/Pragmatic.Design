using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     Static analysis over the call sites the generator already reads: patterns that run but cost more than they
///     should. The generator sees every chain of the project, so it can see one chain running inside a loop over the
///     rows of another: N+1. Syntactic, so a loop over rows obtained another way (a parameter, a field) is read the
///     same, and a false positive is a warning the developer can judge. Indexes: a predicate on a column that is neither the
///     key nor indexed is a scan (PRAG3011, information); the indexes come from the committed schema snapshot (<c>*.schema.json</c>,
///     what the repository's migrations write) where it lists the table, from <c>[Indexed]</c> otherwise.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    /// <summary>The schema snapshots among the AdditionalFiles merged into one index list; null without any. A file that is not a snapshot is PRAG3016 and skipped.</summary>
    private static SchemaIndexes? ReadSchema(ImmutableArray<(string Path, string? Text)> snapshots, List<Diagnostic> diagnostics)
    {
        if (snapshots.IsDefaultOrEmpty)
            return null;
        var schema = new SchemaIndexes();
        foreach (var (path, text) in snapshots)
        {
            string? error = null;
            if (text is null || !schema.Add(text, out error))
                diagnostics.Add(Diagnostic.Create(Diagnostics.SchemaFileInvalid, Location.None, System.IO.Path.GetFileName(path), error ?? "the file could not be read"));
        }

        return schema;
    }

    /// <summary>PRAG3011 for every column the chain's predicates compare without an index, once per column per call site.</summary>
    private static void ReportUnindexed(PlanBuilder builder, CallSite call, EntityModel entity, List<Diagnostic> diagnostics)
    {
        if (entity.IsRawShape)
            return; // the rows of a hand-written statement: no table, no indexes to speak of
        foreach (var column in builder.UnindexedPredicateColumns)
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnindexedPredicate, call.Invocation.GetLocation(), column.Property, entity.Type.Name));
    }

    /// <summary>
    ///     PRAG3010 when the terminal runs inside a <c>foreach</c> (or <c>await foreach</c>) whose variable is a row (an entity or a
    ///     declared DTO) and the chain reads that variable: one query per row. The nearest such loop is reported, once.
    /// </summary>
    private static void ReportQueryInLoop(CallSite call, List<Diagnostic> diagnostics)
    {
        var chain = call.Before.Select(s => s.Invocation).Append(call.Invocation).ToList();
        foreach (var loop in call.Invocation.Ancestors().OfType<ForEachStatementSyntax>())
        {
            if (call.Model.GetDeclaredSymbol(loop) is not ILocalSymbol variable)
                continue;
            var rowType = variable.Type;
            var isRow = EntityModel.IsEntity(rowType) || rowType.GetAttributes().Any(a => a.AttributeClass?.Name == "MapFromAttribute");
            if (!isRow)
                continue;
            var reads = chain.Any(invocation => invocation.DescendantNodes().OfType<IdentifierNameSyntax>().Any(name => name.Identifier.ValueText == variable.Name && SymbolEqualityComparer.Default.Equals(call.Model.GetSymbolInfo(name).Symbol, variable)));
            if (!reads)
                continue;
            diagnostics.Add(Diagnostic.Create(Diagnostics.QueryInLoop, call.Invocation.GetLocation(), call.Name, variable.Name, rowType.Name));
            return;
        }
    }
}
