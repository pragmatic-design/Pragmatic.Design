using Pragmatic.Persistence.Sql;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The declared form: a <c>[Query]</c> static method whose body is one chain on its <c>SqlQuery&lt;T&gt;</c>
///     parameter. It is read once and becomes an extension on <c>SqlSession</c> with the same name and the
///     remaining parameters, whose body puts the parameters' values in the chain's slots and runs the plan:
///     no expression tree per call, the SQL a constant, the command prepared.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private static void EmitQuery(List<DeclaredSectionModel> declared, HashSet<string> usings, List<PlanSectionModel> plans, IMethodSymbol method, System.Func<INamedTypeSymbol, EntityModel?> entities, Compilation compilation, List<Diagnostic> diagnostics, List<CensusEntry> census)
    {
        var where = method.Locations.FirstOrDefault() ?? Location.None;
        void Shape(string why) => diagnostics.Add(Diagnostic.Create(Diagnostics.QueryShape, where, method.Name, why));

        var type = method.ContainingType;
        if (!method.IsStatic || !type.IsStatic || type.ContainingType is not null)
        {
            Shape("the method and its class must be static, and the class not nested");
            return;
        }

        if (!type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<ClassDeclarationSyntax>().Any(c => c.Modifiers.Any(m => m.ValueText == "partial")))
        {
            Shape("the class must be partial, so the generated method can be added to it");
            return;
        }

        if (method.Parameters.Length == 0 || method.Parameters[0].Type is not INamedTypeSymbol { Name: "SqlQuery", TypeArguments.Length: 1 } root || root.TypeArguments[0] is not INamedTypeSymbol entityType || !EntityModel.IsEntity(entityType))
        {
            Shape("the first parameter must be a SqlQuery<T> of an [Entity]");
            return;
        }

        var entity = entities(entityType);
        if (entity is null)
            return;
        var syntax = method.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var body = syntax?.ExpressionBody?.Expression ?? (syntax?.Body is { Statements.Count: 1 } block && block.Statements[0] is ReturnStatementSyntax { Expression: { } returned } ? returned : null);
        if (syntax is not null && body is not null && IsFragment(method))
        {
            EmitFragmentDefinition(method, syntax, body, entityType, entity, entities, compilation, diagnostics); // a declared chain: validated here, generated at its calls
            return;
        }

        if (syntax is null || body is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax terminalAccess } terminal || !Terminals.Contains(terminalAccess.Name.Identifier.ValueText) || terminalAccess.Name.Identifier.ValueText is "ToSql" or "ToCommand")
        {
            Shape("the body must be one expression: the chain on the first parameter ending in an async terminal");
            return;
        }

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var steps = new List<Step>();
        var rooted = CallSite.Walk(terminalAccess.Expression, model, default, steps, 0, parameterRoot: true);
        if (rooted is null || !SymbolEqualityComparer.Default.Equals(rooted, entityType) || model.GetSymbolInfo(terminal).Symbol is not IMethodSymbol terminalMethod)
        {
            Shape("the chain must start at the first parameter and use only SqlQuery operators");
            return;
        }

        var name = terminalAccess.Name.Identifier.ValueText;
        var arguments = terminal.ArgumentList.Arguments;
        var predicateTerminal = name is "FirstOrDefaultAsync" or "CountAsync" or "AnyAsync" && arguments.Count > 0 && arguments[0].Expression is LambdaExpressionSyntax;
        var builder = new PlanBuilder(entity, entities, compilation, valuesInSlots: true);
        builder.Prepare(steps, name);
        foreach (var step in steps)
        {
            if (!builder.AddStep(step, model))
            {
                Shape($"the operator '{step.Name}' in the chain is outside the translated subset{(builder.StepError is { } why ? ": " + why : "")}");
                return;
            }
        }

        if (predicateTerminal && !builder.AddPredicate(model, (LambdaExpressionSyntax)arguments[0].Expression))
        {
            Shape($"the terminal's predicate is outside the translated subset: {builder.LastError ?? "see the diagnostics on it"}");
            return;
        }

        if (name == "ExecuteUpdateAsync" && (arguments.Count < 1 || arguments[0].Expression is not LambdaExpressionSyntax setLambda || !builder.AddSet(model, setLambda, where, diagnostics)))
        {
            Shape("the update's SET list is outside the translated subset (s => s.SetProperty(o => o.Column, value))");
            return;
        }

        if (!builder.AddRules(where, diagnostics) || !builder.TryFinish(name, terminalMethod, arguments, model, where, diagnostics))
            return;
        if (builder.IsAnonymous)
        {
            Shape("the result of a [Query] cannot be an anonymous type: project into a DTO");
            return;
        }

        // The generated method repeats expressions from the definition (captured values, paging), so it needs the definition
        // file's usings; they go to the top of the queries file, and the plan it runs is not file-scoped.
        var planName = $"Query_{type.Name}_{method.Name}";
        var span = where.GetLineSpan();
        plans.Add(builder.ToPlan(planName, fileScoped: false, origin: $"[Query] {type.Name}.{method.Name}, {System.IO.Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1}): {Escape(System.Text.RegularExpressions.Regex.Replace(body.ToString(), @"\s+", " "))}", site: $"{System.IO.Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1}) [Query] {type.Name}.{method.Name}"));
        census.Add(new CensusEntry(System.IO.Path.GetFileName(span.Path), $"{type.Name}.{method.Name}", "[Query]", System.Text.RegularExpressions.Regex.Replace(body.ToString(), @"\s+", " "), builder.Sql(SqlDialect.Sqlite), builder.Sql(SqlDialect.PostgreSql), builder.Sql(SqlDialect.SqlServer), builder.Sql(SqlDialect.MySql), builder.Sql(SqlDialect.Oracle), span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1));
        foreach (var directive in syntax.SyntaxTree.GetCompilationUnitRoot().Usings.Concat(syntax.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().SelectMany(n => n.Usings)))
            usings.Add(directive.ToString());

        // The generated body: the chain's values in the slots, the paging as written, the plan run.
        string Chain(string root)
            => $"{root}.Query<{entity.FullName}>()"
               + string.Concat(builder.ValueSlots.Select((value, i) => $".WithSlot({i}, {value})"))
               + string.Concat(builder.SkipTexts.Select(skip => $".Skip({skip})"))
               + string.Concat(builder.TakeTexts.Select(take => $".Take({take})"))
               + (builder.ChainElement != entity.FullName ? $".As<{builder.ChainElement}>()" : "");

        var chain = Chain("session");
        var plan = $"global::Pragmatic.Persistence.Sql.Generated.{planName}.Instance";
        var (returnType, execute) = builder.Terminal;
        string call;
        string? batchRead = null; // the plan's reader off a batch's reader, for the batch overload; null where the query cannot join one
        if (builder.IsAggregate)
        {
            returnType = $"global::System.Threading.Tasks.ValueTask<{builder.ElementType}>";
            call = $"{plan}.ScalarAsync({chain}.As<{builder.ElementType}>(), ct)";
            batchRead = $"static (plan, q, r, d, ct) => plan.ReadScalarAsync(q, r, d, ct)";
        }
        else if (name == "ToDictionaryAsync")
        {
            returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            call = $"{plan}.ToDictionaryAsync({chain}, {arguments[0]}, ct)";
            batchRead = $"static (plan, q, r, d, ct) => plan.ReadDictionaryAsync(q, {arguments[0]}, r, d, ct)";
        }
        else
        {
            call = $"{plan}.{execute}({chain}, ct)";
            var reader = execute switch
            {
                "ToListAsync" => "ReadListAsync",
                "ToArrayAsync" => "ReadArrayAsync",
                "FirstOrDefaultAsync" => "ReadFirstOrDefaultAsync",
                "FirstAsync" => "ReadFirstAsync",
                "SingleAsync" => "ReadSingleAsync",
                "SingleOrDefaultAsync" => "ReadSingleOrDefaultAsync",
                "CountAsync" => "ReadCountAsync",
                "AnyAsync" => "ReadAnyAsync",
                "ExecuteAsync" => "ReadCommandAsync", // a command: its rows touched, counted in the batch
                _ => null, // a stream or a sync: not batched
            };
            if (reader is not null)
                batchRead = $"static (plan, q, r, d, ct) => plan.{reader}(q, r, d, ct)";
        }

        // The batch overload: the same query recorded on a SqlBatch, answered by its item once the batch has run.
        const string valueTask = "global::System.Threading.Tasks.ValueTask<";
        var resultType = returnType.StartsWith(valueTask, System.StringComparison.Ordinal) ? returnType.Substring(valueTask.Length, returnType.Length - valueTask.Length - 1) : null;
        var batched = batchRead is not null && resultType is not null;
        declared.Add(new DeclaredQueryModel(
            NamespaceOf(type),
            type.Name,
            method.Name,
            method.Parameters.Select(p => p.Type.ToDisplayString()).ToEquatableArray(),
            builder.Sql(SqlDialect.PostgreSql),
            returnType,
            method.Parameters.Skip(1).Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}").ToEquatableArray(),
            call,
            batched ? resultType : null,
            batched ? $"batch.Add({plan}, {Chain("batch.Session")}{(builder.IsAggregate ? $".As<{builder.ElementType}>()" : "")}, {batchRead}{(execute == "ExecuteAsync" ? ", counted: true" : "")})" : null));
        diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, "[Query] " + method.Name, builder.DeclaredSql(SqlDialect.Sqlite)));
    }

    /// <summary>A text for a doc comment: the markup characters escaped.</summary>
    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
