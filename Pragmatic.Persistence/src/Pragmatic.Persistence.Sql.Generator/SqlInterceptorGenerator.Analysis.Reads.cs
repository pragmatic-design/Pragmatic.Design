using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     More static analysis over the call sites (information, promoted to warnings by a census build), each a pattern that runs
///     but costs more than it should: a list read with no bound (PRAG3021), a LIKE with a leading wildcard on an indexed column
///     (PRAG3022), a predicate that reads no row (PRAG3023), an included row of which one column is read (PRAG3024), several
///     writes in a method with no transaction around them (PRAG3025). Syntactic where it reads the method around the call
///     site, as the N+1 analysis is: a false positive is a note the developer can judge.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    private static readonly HashSet<string> WriteMethods = ["InsertAsync", "InsertManyAsync", "UpdateAsync", "UpsertAsync", "DeleteAsync", "SaveAsync", "SaveManyAsync", "SaveGraphAsync", "DeleteGraphAsync", "LinkAsync", "UnlinkAsync", "ExecuteUpdateAsync", "ExecuteDeleteAsync", "ExecuteInsertAsync", "SyncAsync"];

    /// <summary>PRAG3021: a list terminal with no Take and no predicate on the key, on an entity that is not a small lookup table.</summary>
    private static void ReportUnbounded(PlanBuilder builder, CallSite call, EntityModel entity, List<Diagnostic> diagnostics)
    {
        if (call.Name is not ("ToListAsync" or "ToArrayAsync" or "ToDictionaryAsync") || builder.IsBounded || entity.Small || entity.IsRawShape)
            return;
        diagnostics.Add(Diagnostic.Create(Diagnostics.UnboundedRead, call.Invocation.GetLocation(), call.Name, entity.Type.Name));
    }

    /// <summary>PRAG3022: every indexed column the chain compares with a LIKE that starts with a wildcard.</summary>
    private static void ReportLeadingWildcards(PlanBuilder builder, CallSite call, EntityModel entity, List<Diagnostic> diagnostics)
    {
        foreach (var (property, method) in builder.LeadingWildcards)
            diagnostics.Add(Diagnostic.Create(Diagnostics.LeadingWildcard, call.Invocation.GetLocation(), property, entity.Type.Name, method));
    }

    /// <summary>PRAG3023 when the lambda's body never reads its row parameter: the predicate is the same for every row.</summary>
    private static void ReportConstantPredicate(SemanticModel model, LambdaExpressionSyntax lambda, List<Diagnostic> diagnostics)
    {
        if (!TryParameters(model, lambda, out var parameter, out _))
            return;
        var body = Body(lambda);
        var readsRow = body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(name => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, parameter));
        if (!readsRow)
            diagnostics.Add(Diagnostic.Create(Diagnostics.ConstantPredicate, lambda.GetLocation(), System.Text.RegularExpressions.Regex.Replace(lambda.ToString(), @"\s+", " ")));
    }

    /// <summary>
    ///     PRAG3024: a to-one Include whose target the enclosing method reads through one member only (<c>row.Customer.Name</c>, with
    ///     <c>!</c> or <c>?.</c> in between): the row was loaded for one column. A use of the navigation as a whole (passed, assigned,
    ///     compared) says nothing about which columns are needed, and silences the report.
    /// </summary>
    private static void ReportIncludeReadOnce(CallSite call, EntityModel entity, List<Diagnostic> diagnostics)
    {
        var scope = EnclosingBody(call.Invocation);
        if (scope is null)
            return;
        foreach (var step in call.Before.Where(s => s.Name == "Include"))
        {
            if (step.Lambda is null || Body(step.Lambda) is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax } included || entity.Navigation(included.Name.Identifier.ValueText) is not { } navigation)
                continue; // a collection or a path below one: the loader's business, not this analysis
            var members = new HashSet<string>();
            var whole = false;
            var reads = 0;
            foreach (var access in scope.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (access.Name.Identifier.ValueText != navigation.Property || call.Invocation.Span.Contains(access.Span))
                    continue; // the chain's own lambdas are the query, not a read of its rows
                if (call.Model.GetTypeInfo(access.Expression).Type is not INamedTypeSymbol receiver || !SymbolEqualityComparer.Default.Equals(receiver, entity.Type))
                    continue;
                reads++;
                SyntaxNode node = access;
                while (node.Parent is ParenthesizedExpressionSyntax || node.Parent is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
                    node = node.Parent;
                if (node.Parent is MemberAccessExpressionSyntax outer && outer.Expression == node)
                    members.Add(outer.Name.Identifier.ValueText);
                else if (node.Parent is ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax binding } conditional && conditional.Expression == node)
                    members.Add(binding.Name.Identifier.ValueText);
                else
                    whole = true;
            }

            if (reads > 0 && !whole && members.Count == 1)
                diagnostics.Add(Diagnostic.Create(Diagnostics.IncludeReadOnce, step.Invocation.GetLocation(), navigation.Property, navigation.Target.Name, entity.Type.Name, members.First()));
        }
    }

    /// <summary>
    ///     PRAG3025 at the first write of a method that runs two or more (the chain's command terminals, the session's write methods) and
    ///     opens no transaction, joins none (<c>session.Transaction</c>) and batches nothing: a failure between two leaves the first applied.
    /// </summary>
    private static void ReportWritesOutsideTransaction(CallSite call, List<Diagnostic> diagnostics)
    {
        if (call.Name is not ("ExecuteUpdateAsync" or "ExecuteDeleteAsync" or "ExecuteInsertAsync" or "SyncAsync"))
            return;
        var scope = EnclosingBody(call.Invocation);
        if (scope is null)
            return;
        var writes = scope.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(i => i.Expression is MemberAccessExpressionSyntax m && WriteMethods.Contains(m.Name.Identifier.ValueText));
        if (writes < 2)
            return;
        if (diagnostics.Any(d => d.Id == Diagnostics.WritesOutsideTransaction.Id && d.Location.SourceTree == call.Invocation.SyntaxTree && scope.Span.Contains(d.Location.SourceSpan)))
            return; // reported once per method, at the first of its writes the generator translates
        var guarded = scope.DescendantNodes().Any(n =>
            n is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax guard } && guard.Name.Identifier.ValueText is "BeginTransaction" or "TransactAsync" or "WriteBatch" or "Batch"
            || n is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Transaction" });
        if (guarded)
            return;
        var method = scope.Parent switch
        {
            MethodDeclarationSyntax m => $"{(m.Parent as TypeDeclarationSyntax)?.Identifier.ValueText}.{m.Identifier.ValueText}",
            ConstructorDeclarationSyntax c => $"{c.Identifier.ValueText}()",
            LocalFunctionStatementSyntax f => f.Identifier.ValueText,
            _ => "the method",
        };
        diagnostics.Add(Diagnostic.Create(Diagnostics.WritesOutsideTransaction, call.Invocation.GetLocation(), method, writes));
    }

    /// <summary>The body of the method, constructor, local function or lambda the node is in; null at the top level.</summary>
    private static SyntaxNode? EnclosingBody(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case MethodDeclarationSyntax method:
                    return (SyntaxNode?)method.Body ?? method.ExpressionBody;
                case ConstructorDeclarationSyntax constructor:
                    return (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody;
                case LocalFunctionStatementSyntax local:
                    return (SyntaxNode?)local.Body ?? local.ExpressionBody;
                case AnonymousFunctionExpressionSyntax lambda:
                    return (SyntaxNode?)lambda.Block ?? lambda.ExpressionBody;
            }
        }

        return null;
    }
}
