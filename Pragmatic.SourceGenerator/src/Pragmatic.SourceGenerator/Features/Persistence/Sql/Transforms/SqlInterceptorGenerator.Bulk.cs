using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Pragmatic's bulk writes under the Sql engine: every <c>IBulkRepository&lt;T&gt;.BulkUpdateAsync</c> and <c>BulkDeleteAsync</c> written in this
///     compilation on an entity the Sql engine serves is compiled where it is written, into one statement: the rows the specification selects
///     (composed at runtime, as <c>Matching</c>'s predicate) and, for an update, the SET list of the setters' lambda, its values recorded when the
///     delegate runs. A <c>[SoftDelete]</c> entity's delete is the marking of its rows (the flag, the instant, the caller), as the unit of work
///     marks one row, and the erasure inside <c>SoftDeleteScope.Suspend</c>: both are compiled, and <c>SqlBulk</c> picks one when the call runs.
///     The call is intercepted to run the plan through <c>SqlBulk</c>, on the session of the repository's unit of work.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private const string BulkRepository = "Pragmatic.Persistence.Repository.IBulkRepository`1";

    private static void EmitBulk(List<InterceptorModel> interceptors, List<PlanSectionModel> plans, Compilation compilation, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
    {
        if (compilation.GetTypeByMetadataName("Pragmatic.Persistence.Sql.SqlBulk") is null || compilation.GetTypeByMetadataName(BulkRepository) is not { } bulk)
            return;
        var count = 0;
        var engines = PragmaticEngines.Of(compilation);
        foreach (var tree in compilation.SyntaxTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "BulkUpdateAsync" or "BulkDeleteAsync" } access
                    || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
                    || BulkEntity(method, bulk) is not { } entityType
                    || !engines.Serves(entityType, PragmaticEngine.Sql) // another engine's entity: its own repository runs the call
                    || lookup(entityType) is not { } entity
                    || model.GetInterceptableLocation(invocation) is not { } location)
                    continue;
                var update = method.Name == "BulkUpdateAsync";
                var where = invocation.GetLocation();
                if (entity.RollUpParents.Count > 0)
                {
                    // The plan would refuse it too, naming the statement it builds; this names the call written and the repository's way.
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, method.Name,
                        $"{entityType.Name} feeds [RollUp] aggregates ({string.Join(", ", entity.RollUpParents)}): a set-based write would leave them stale; load the rows and change them through the unit of work, whose save keeps them"));
                    continue;
                }

                LambdaExpressionSyntax? setters = null;
                if (update && (invocation.ArgumentList.Arguments.Count < 2 || (setters = invocation.ArgumentList.Arguments[1].Expression as LambdaExpressionSyntax) is null))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, invocation.ArgumentList.ToString(), "set", "the setters are a lambda written at the call: s => s.SetProperty(o => o.Column, value), the SET list read from it"));
                    continue;
                }

                var site = BulkSite(invocation);
                var written = Regex.Replace(invocation.ToString(), @"\s+", " ");
                var origin = $"{site}: {Escape(written.Length > 240 ? written.Substring(0, 240) + "…" : written)}";
                var type = entity.FullName;

                // One plan: the update, the delete, or a [SoftDelete] entity's marking. Null when it was refused (the diagnostic reported).
                PlanBuilder? Plan(bool mark)
                {
                    var builder = new PlanBuilder(entity, lookup, compilation, valuesInSlots: true);
                    builder.AddRuntimePredicate("__predicate");
                    if (setters is not null && !builder.AddRecordedSet(model, setters, where, diagnostics))
                        return null;
                    if (mark)
                        builder.AddSoftDeleteMark();
                    else if (!update)
                        builder.AllowErasure(); // a [SoftDelete] entity's erasure runs only inside SoftDeleteScope.Suspend
                    var terminal = update || mark ? "ExecuteUpdateAsync" : "ExecuteDeleteAsync";
                    return builder.AddRules(where, diagnostics) && builder.TryFinish(terminal, method, invocation.ArgumentList.Arguments, model, where, diagnostics) ? builder : null;
                }

                // The plan added, its translation reported; the run that hands it its slots.
                string Run(PlanBuilder builder, string planName)
                {
                    plans.Add(builder.ToPlan(planName, origin: origin, site: site));
                    diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, $"{method.Name} on {entityType.Name}", builder.DeclaredSql(global::Pragmatic.Persistence.Sql.SqlDialect.Sqlite)));
                    var chain = $"session.Query<{type}>()" + string.Concat(builder.ValueSlots.Select((value, i) => $".WithSlot({i}, {value})"));
                    return $"static (session, __predicate, __values, ct) => {planName}.Instance.ExecuteAsync({chain}, ct)";
                }

                // A [SoftDelete] entity's delete has both plans, the marking and the erasure: SqlBulk runs the erasure inside SoftDeleteScope.Suspend.
                var marks = !update && entity.SoftDeleteColumn is not null;
                var marking = marks ? Plan(mark: true) : null;
                if (marks && marking is null || Plan(mark: false) is not { } plan)
                    continue;

                var mark = marking is null ? null : Run(marking, $"Bulk_{count}_Mark");
                var run = Run(plan, $"Bulk_{count}");

                var receiver = $"this {method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} repository";
                var parameters = method.Parameters.Select(p => $"{p.Type.ToDisplayString(Annotated)} {p.Name}{(p.HasExplicitDefaultValue ? " = default" : "")}");
                var body = update
                    ? $"global::Pragmatic.Persistence.Sql.SqlBulk.UpdateAsync(repository, filter, set, {run}, ct)"
                    : $"global::Pragmatic.Persistence.Sql.SqlBulk.DeleteAsync(repository, filter, {mark ?? "null"}, {run}, ct)";
                interceptors.Add(new(location.GetInterceptsLocationAttributeSyntax(), "global::System.Threading.Tasks.Task<int>", $"{method.Name}_{count}", "", [receiver, .. parameters], body, Comment: site, BodyOnNextLine: true));
                count++;
            }
        }
    }

    /// <summary>
    ///     The entity of a bulk write: the method is <c>IBulkRepository&lt;T&gt;</c>'s, or a repository's implementation of it (a call on the
    ///     concrete type). Null for any other method of that name.
    /// </summary>
    internal static INamedTypeSymbol? BulkEntity(IMethodSymbol method, INamedTypeSymbol bulk)
    {
        var owner = method.ContainingType;
        if (SymbolEqualityComparer.Default.Equals(owner.OriginalDefinition, bulk))
            return owner.TypeArguments[0] as INamedTypeSymbol;
        foreach (var contract in owner.AllInterfaces.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, bulk)))
        {
            foreach (var member in contract.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(owner.FindImplementationForInterfaceMember(member), method))
                    return contract.TypeArguments[0] as INamedTypeSymbol;
            }
        }

        return null;
    }

    /// <summary>The call site, as a plan's origin names it: <c>File.cs(line,col) in Type.Method</c>.</summary>
    private static string BulkSite(InvocationExpressionSyntax invocation)
    {
        var span = invocation.GetLocation().GetLineSpan();
        var member = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var type = member?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var where = member is null ? "" : $" in {type?.Identifier.ValueText}.{member.Identifier.ValueText}";
        return $"{System.IO.Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}){where}";
    }
}
