using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     Pragmatic's specifications under the Sql engine: every <c>Spec&lt;T&gt;.Where(predicate)</c> written in this compilation on a Pragmatic
///     entity is compiled where it is written, into a <c>SqlCompiledPredicate</c>, and the call is intercepted to build a
///     <c>CompiledSpecification&lt;T&gt;</c>: the same specification to every reader (its expression is the lambda), carrying the compiled
///     predicate and the values the lambda captured. A composition of them (<c>&amp;</c>, <c>|</c>, <c>!</c>) runs on the Sql engine through
///     <c>SqlSpecifications.ToPredicate</c>. The call site is the definition, not the use: the uses are often in code Pragmatic's generator
///     writes, which no other generator sees.
/// </summary>
public sealed partial class SqlInterceptorGenerator
{
    private const string CompiledSpecification = "Pragmatic.Persistence.Sql.Pragmatic.CompiledSpecification`1";

    /// <summary>The compiled predicates (a class of fields, in <paramref name="predicates" />) and the interceptors of the calls that build them.</summary>
    private static void EmitSpecifications(StringBuilder interceptors, StringBuilder predicates, Compilation compilation, Func<INamedTypeSymbol, EntityModel?> lookup, List<Diagnostic> diagnostics)
    {
        if (compilation.GetTypeByMetadataName(CompiledSpecification) is null || compilation.GetTypeByMetadataName("Pragmatic.Specification.Spec`1") is not { } spec)
            return;
        var fields = new StringBuilder();
        var count = 0;
        var engines = PragmaticEngines.Of(compilation);
        foreach (var tree in compilation.SyntaxTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Where" }
                    || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
                    || !SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, spec)
                    || method.ContainingType.TypeArguments[0] is not INamedTypeSymbol entityType
                    || !PragmaticShape.IsPragmaticEntity(entityType)
                    || !engines.Serves(entityType, PragmaticEngine.Sql) // another engine's entity: its generator compiles the leaf
                    || lookup(entityType) is not { } entity)
                    continue;
                if (CompileLeaf(invocation, model, entity, lookup, out var why) is not var (texts, kinds, values))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.SpecificationNotCompiled, invocation.GetLocation(), entityType.Name, why));
                    continue;
                }

                if (model.GetInterceptableLocation(invocation) is not { } location)
                    continue;
                var type = entity.FullName;
                fields.AppendLine($"        internal static readonly global::Pragmatic.Persistence.Sql.SqlCompiledPredicate P{count} = new({Literal(texts[0])}, {Literal(texts[1])}, {Literal(texts[2])}, {Literal(texts[3])}, {Literal(texts[4])}, new global::Pragmatic.Persistence.Sql.SqlParamKind[] {{ {string.Join(", ", kinds.Select(k => "global::Pragmatic.Persistence.Sql.SqlParamKind." + k))} }});");
                interceptors.AppendLine($"        {location.GetInterceptsLocationAttributeSyntax()}");
                interceptors.AppendLine($"        internal static global::Pragmatic.Specification.Specification<{type}> SpecWhere_{count}(Expression<Func<{type}, bool>> predicate)");
                interceptors.AppendLine($"            => new global::Pragmatic.Persistence.Sql.Pragmatic.CompiledSpecification<{type}>(CompiledPredicates.P{count}, new object?[] {{ {string.Join(", ", values)} }}, predicate);");
                interceptors.AppendLine();
                diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, invocation.GetLocation(), $"Spec<{entityType.Name}>.Where", texts[0]));
                count++;
            }
        }

        if (count == 0)
            return;
        predicates.AppendLine("    /// <summary>The predicates of the specifications written in this compilation, compiled where they are written.</summary>");
        predicates.AppendLine("    file static class CompiledPredicates");
        predicates.AppendLine("    {");
        predicates.Append(fields);
        predicates.AppendLine("    }");
        predicates.AppendLine();
    }

    /// <summary>
    ///     The predicate of <c>Spec&lt;T&gt;.Where(lambda)</c> on the entity's row: its texts per dialect, its values' kinds, and the C# that reads
    ///     each value off the lambda's tree (a constant as a literal). Null, with the reason, when it is not a lambda written here, reads a
    ///     navigation (a join: not a leaf yet) or is outside the translated subset.
    /// </summary>
    private static (string[] Texts, IReadOnlyList<string> Kinds, List<string> Values)? CompileLeaf(InvocationExpressionSyntax invocation, SemanticModel model, EntityModel entity,
        Func<INamedTypeSymbol, EntityModel?> lookup, out string why)
    {
        why = "";
        if (invocation.ArgumentList.Arguments.Count != 1 || invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda || !TryParameter(model, lambda, out var parameter))
        {
            why = "the predicate must be a lambda written at the call (an expression held in a variable has no source to translate)";
            return null;
        }

        var translator = new LambdaTranslator(model, lookup, new EntityRow(entity, "t"), parameter);
        var predicate = translator.Predicate(Body(lambda));
        if (predicate is null)
        {
            why = $"'{translator.ErrorExpression ?? lambda.ToString()}' {translator.Error ?? "is outside the translated subset"}";
            return null;
        }

        if (translator.Joins.Count > 0)
        {
            why = "it reads a navigation, which a compiled specification does not join yet: compare the foreign key, or filter in a [Query]"; // INVESTIGATE: navigations as an EXISTS
            return null;
        }

        var values = new List<string>();
        for (var i = 0; i < translator.ParameterPaths.Count; i++)
            values.Add(translator.ParameterIsConstant[i] ? translator.ParameterTexts[i] : "global::Pragmatic.Persistence.Sql." + translator.ParameterPaths[i].Replace("(lambda)", "(predicate)"));
        return (PlanBuilder.CompileLeaf(translator, predicate), translator.ParameterKinds, values);
    }
}
