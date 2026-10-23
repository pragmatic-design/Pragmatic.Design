using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The terminals: the whole chain becomes a plan (see <see cref="PlanBuilder" />), and the terminal call site an interceptor that runs it.</summary>
public sealed partial class SqlInterceptorGenerator
{
    private static bool EmitTerminal(List<InterceptorModel> source,List<PlanSectionModel> plans, CallSite call, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, Func<string, EntityModel?> tables, Compilation compilation, int index, string attribute, List<Diagnostic> diagnostics, List<CensusEntry> census)
    {
        var name = call.Name;
        var where = call.Invocation.GetLocation();
        var arguments = call.Invocation.ArgumentList.Arguments;
        var predicateTerminal = name is "FirstOrDefaultAsync" or "CountAsync" or "AnyAsync" && arguments.Count > 0 && arguments[0].Expression is LambdaExpressionSyntax;
        // A deferred terminal (ToList(batch), Count(batch)…): the plan of its async twin, recorded on the batch instead of run.
        var deferred = BatchTerminals.TryGetValue(name, out var twin) ? twin : null;

        var builder = new PlanBuilder(entity, entities, compilation, valuesInSlots: false);
        builder.Prepare(call.Before, deferred ?? name);
        foreach (var step in call.Before)
        {
            if (builder.AddStep(step, call.Model))
            {
                if (step.Name == "Sql" && builder.RawText is { } raw && RawSqlCheck.Problems(raw, entity, tables) is { Count: > 0 } problems)
                {
                    // The hand-written statement against the model: a table or a column the entities do not have, a column the row type reads and the statement does not return.
                    diagnostics.Add(Diagnostic.Create(Diagnostics.RawStatementMismatch, step.Invocation.GetLocation(), string.Join("; ", problems)));
                    return false;
                }

                continue;
            }

            if (step.Name == "Sql")
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, step.Invocation.GetLocation(), step.Invocation.ArgumentList.ToString(), "statement", PlanBuilder.RawShape)); // the root has no interceptor of its own
            else if (builder.StepError is { } why)
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, step.Invocation.GetLocation(), step.Name, why)); // a step after a set operation: valid on its own, not there
            return false; // otherwise reported at the operator's own call site
        }

        if (predicateTerminal)
            ReportConstantPredicate(call.Model, (LambdaExpressionSyntax)arguments[0].Expression, diagnostics);
        if (predicateTerminal && !builder.AddPredicate(call.Model, (LambdaExpressionSyntax)arguments[0].Expression))
        {
            diagnostics.Add(Diagnostics.Untranslated(where, builder.LastErrorExpression ?? arguments[0].ToString(), "predicate", builder.LastError ?? "the predicate could not be translated"));
            return false;
        }

        var setTerminal = name == "ExecuteUpdateAsync";
        if (setTerminal && (arguments.Count < 1 || arguments[0].Expression is not LambdaExpressionSyntax setLambda || !builder.AddSet(call.Model, setLambda, where, diagnostics)))
        {
            if (arguments.Count < 1 || arguments[0].Expression is not LambdaExpressionSyntax)
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, arguments.Count > 0 ? arguments[0].ToString() : name, "set", "ExecuteUpdateAsync takes a lambda: s => s.SetProperty(o => o.Column, value)"));
            return false;
        }

        // The rows read back by an update or delete (the overloads with a TResult): the lambda after the SET list, or the first of a delete.
        var returning = name is "ExecuteUpdateAsync" or "ExecuteDeleteAsync" && call.Method.TypeParameters.Any(p => p.Name == "TResult");
        if (returning)
        {
            var at = setTerminal ? 1 : 0;
            if (arguments.Count <= at || arguments[at].Expression is not LambdaExpressionSyntax returningLambda)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, arguments.Count > at ? arguments[at].ToString() : name, "returning", "the rows read back are a lambda over the row: o => o, or o => new Dto { … }"));
                return false;
            }

            if (!builder.AddReturning(call.Model, returningLambda, call.Method.TypeArguments[call.Method.TypeArguments.Length - 1], where, diagnostics))
                return false;
        }

        var insertSelect = name == "ExecuteInsertAsync";
        if (insertSelect && (arguments.Count < 1 || arguments[0].Expression is not LambdaExpressionSyntax rowsLambda || !builder.AddInsertSelect(call.Model, rowsLambda, call.Method.TypeArguments.Length == 1 ? call.Method.TypeArguments[0] as INamedTypeSymbol : null, where, diagnostics)))
        {
            if (arguments.Count < 1 || arguments[0].Expression is not LambdaExpressionSyntax)
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, arguments.Count > 0 ? arguments[0].ToString() : name, "rows", "ExecuteInsertAsync takes a lambda building the target's row: o => new Target { Column = o.Column }"));
            return false;
        }

        if (!builder.AddRules(where, diagnostics) || !builder.TryFinish(deferred ?? name, call.Method, arguments, call.Model, where, diagnostics))
            return false;

        var planType = builder.PlanType($"Plan_{index}");
        var origin = Origin(call);
        plans.Add(builder.ToPlan($"Plan_{index}", origin: origin, site: Site(call)));

        var elementType = builder.ElementType;
        var typeParameters = builder.IsAnonymous ? "<TElement>" : "";
        var receiver = On(builder.ChainElement);
        var (returnType, execute) = builder.Terminal;
        const string token = "global::System.Threading.CancellationToken ct = default";
        InterceptorModel Terminal(string returns, string method, string generics, EquatableArray<string> parameters, string body, string constraint = "")
            => new(attribute, returns, method, generics, parameters, body, constraint, Comment: origin);
        if (deferred is not null)
        {
            // Recorded on the batch with the chain's values; the item answers once the batch has run (the plan's reader over the batch's reader).
            var read = deferred switch { "ToListAsync" => "ReadListAsync", "FirstOrDefaultAsync" => "ReadFirstOrDefaultAsync", "CountAsync" => "ReadCountAsync", _ => "ReadAnyAsync" };
            const string valueTask = "global::System.Threading.Tasks.ValueTask<";
            var itemType = returnType.Substring(valueTask.Length, returnType.Length - valueTask.Length - 1);
            source.Add(Terminal($"global::Pragmatic.Persistence.Sql.SqlBatchItem<{itemType}>", $"{name}_{index}", typeParameters, [receiver, "global::Pragmatic.Persistence.Sql.SqlBatch batch"], $"batch.Add({planType}.Instance, q, static (plan, query, r, d, ct) => plan.{read}(query, r, d, ct))"));
        }
        else if (name == "ToSql")
            source.Add(Terminal("string", $"ToSql_{index}", typeParameters, [receiver], $"{planType}.Instance.RunnableSql(q.Session)")); // the session's dialect as its database runs it, and its schema when it has one
        else if (name == "ToCommand")
            source.Add(Terminal("global::System.Data.Common.DbCommand", $"ToCommand_{index}", typeParameters, [receiver], $"{planType}.Instance.ToCommand(q)"));
        else if (builder.IsAggregate)
            source.Add(Terminal(returnType, $"{name}_{index}", "", [receiver, $"Func<{builder.ChainElement}, {call.Method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}> value", token], $"{planType}.Instance.ScalarAsync(q.As<{elementType}>(), ct)"));
        else if (name == "ToDictionaryAsync")
        {
            var keyType = builder.IsAnonymous ? "TKey" : call.Method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            source.Add(Terminal($"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.Dictionary<{keyType}, {elementType}>>", $"ToDictionaryAsync_{index}", builder.IsAnonymous ? "<TElement, TKey>" : "", [receiver, $"Func<{elementType}, {keyType}> key", token], $"{planType}.Instance.ToDictionaryAsync(q, key, ct)", builder.IsAnonymous ? " where TKey : notnull" : ""));
        }
        else if (name == "ShapeAsync")
            source.Add(Terminal(returnType, $"ShapeAsync_{index}", typeParameters, [receiver, "global::Pragmatic.Persistence.Sql.SqlShape shape", token], $"{planType}.Instance.ShapeAsync(q, shape, ct)"));
        else if (name == "SyncAsync")
            source.Add(Terminal(returnType, $"SyncAsync_{index}", "", [receiver, $"global::System.Collections.Generic.IReadOnlyList<{builder.ChainElement}> rows", token], $"{planType}.Instance.SyncAsync(q, rows, ct)"));
        else if (predicateTerminal || setTerminal || returning || insertSelect)
        {
            // The call's own parameters (the lambda, the argument of the two-parameter form, the token): the argument goes to the chain's last slot.
            var parameters = call.Method.Parameters.Select(p => $"{p.Type.ToDisplayString(Annotated)} {p.Name}{(p.HasExplicitDefaultValue ? " = default" : "")}");
            var chain = TakesArg(call.Method) ? $"q.WithSlot({builder.Slots - 1}, arg)" : "q";
            if (returning)
                chain += $".As<{elementType}>()"; // the rows read back are the plan's element, the chain's rows the command's
            source.Add(Terminal(returnType, $"{name}_{index}", typeParameters, [receiver, .. parameters], $"{planType}.Instance.{execute}({chain}, ct)"));
        }
        else
            source.Add(Terminal(returnType, $"{name}_{index}", typeParameters, [receiver, token], $"{planType}.Instance.{execute}(q, ct)"));
        diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, builder.DeclaredSql(SqlDialect.Sqlite)));
        census.Add(CensusOf(call, name, builder));
        ReportQueryInLoop(call, diagnostics);
        ReportUnindexed(builder, call, entity, diagnostics);
        ReportUnbounded(builder, call, entity, diagnostics);
        ReportLeadingWildcards(builder, call, entity, diagnostics);
        ReportIncludeReadOnce(call, entity, diagnostics);
        ReportWritesOutsideTransaction(call, diagnostics);
        foreach (var (function, dialect, note) in builder.TerminalEmulations)
            diagnostics.Add(Diagnostic.Create(Diagnostics.Emulated, where, function, dialect, note));
        return true;
    }

    /// <summary>
    ///     Where a plan comes from, for the generated file's reader: <c>File.cs(line,col) in Type.Method: the chain as written</c>, the chain's
    ///     whitespace collapsed and its text cut at 240 characters, escaped for a doc comment.
    /// </summary>
    internal static string Origin(CallSite call)
    {
        var chain = Regex.Replace(call.Invocation.ToString(), @"\s+", " ");
        if (chain.Length > 240)
            chain = chain.Substring(0, 240) + "…";
        return $"{Site(call)}: {Escape(chain)}";
    }

    /// <summary>The call site alone: <c>File.cs(line,col) in Type.Method</c>, the plan's origin at runtime (its metrics' tag, the comment on its statements).</summary>
    internal static string Site(CallSite call)
    {
        var span = call.Invocation.GetLocation().GetLineSpan();
        var file = System.IO.Path.GetFileName(span.Path);
        var position = $"{file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
        var member = call.Invocation.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault(m => m is MethodDeclarationSyntax or PropertyDeclarationSyntax or ConstructorDeclarationSyntax);
        var type = member?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var where = member switch
        {
            MethodDeclarationSyntax method => $" in {type?.Identifier.ValueText}.{method.Identifier.ValueText}",
            PropertyDeclarationSyntax property => $" in {type?.Identifier.ValueText}.{property.Identifier.ValueText}",
            ConstructorDeclarationSyntax constructor => $" in {constructor.Identifier.ValueText}()",
            _ => "", // top-level statements
        };
        return position + where;
    }

    /// <summary>The lambda of a rule's <c>ToExpression</c>, when the rule is declared in this compilation with an expression body.</summary>
    internal static LambdaExpressionSyntax? RuleLambda(INamedTypeSymbol rule, Compilation compilation, out SemanticModel? model)
    {
        model = null;
        var method = rule.GetMembers("ToExpression").OfType<IMethodSymbol>().FirstOrDefault();
        if (method is null)
            return null;
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is MethodDeclarationSyntax { ExpressionBody.Expression: LambdaExpressionSyntax lambda })
            {
                model = compilation.GetSemanticModel(reference.SyntaxTree);
                return lambda;
            }
        }

        return null;
    }
}
