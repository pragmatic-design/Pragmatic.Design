using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     Pragmatic's specifications under the documents engine (the Sql engine's <c>SqlInterceptorGenerator.Specifications</c>, for documents): every
///     <c>Spec&lt;T&gt;.Where(predicate)</c> written on a Pragmatic entity the documents engine serves is compiled where it is written, into a
///     <c>DocCompiledPredicate</c> (its MongoDB conditions, its Cosmos DB text and values), and the call is intercepted to build a
///     <c>DocCompiledSpecification&lt;T&gt;</c>: the same specification to every reader, carrying the condition and the values the lambda captured
///     (read off its tree, <see cref="DocTreePaths" />). A composition of them runs through <c>DocSpecifications.ToPredicate</c> and <c>Matching</c>.
/// </summary>
internal static partial class DocumentGenerator
{
    private const string CompiledSpecification = "Pragmatic.Persistence.Documents.DocCompiledSpecification`1";

    /// <summary>The compiled specifications of <paramref name="compilation" />'s trees (the shape's and the generated repositories' among them).</summary>
    private static List<DocSpecificationModel> Specifications(Compilation compilation, Func<INamedTypeSymbol, DocModel?> models, List<Diagnostic> diagnostics)
    {
        var specifications = new List<DocSpecificationModel>();
        if (compilation.GetTypeByMetadataName(CompiledSpecification) is null || compilation.GetTypeByMetadataName("Pragmatic.Specification.Spec`1") is not { } spec)
            return specifications;
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
                    || !DocModel.IsPragmatic(entityType)
                    || !engines.Serves(entityType, PragmaticEngine.Documents) // another engine's entity: its generator compiles the leaf
                    || models(entityType) is not { } entity)
                    continue;
                if (invocation.ArgumentList.Arguments.Count != 1 || invocation.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } lambda
                    || model.GetDeclaredSymbol(lambda.Parameter) is not IParameterSymbol parameter)
                {
                    diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, invocation.GetLocation(), $"Spec<{entityType.Name}>.Where", "the predicate is a lambda written at the call (an expression held in a variable has no source to translate)"));
                    continue;
                }

                var captures = new List<string>();
                var translator = new DocTranslator(model, entity, models, parameter, null, null, diagnostics, invocation.GetLocation(), DocTreePaths.Of(lambda, model, "predicate"), captures);
                var condition = translator.Condition(body);
                if (condition is null || translator.Failed || model.GetInterceptableLocation(invocation) is not { } location)
                    continue;

                var mongo = new StringBuilder();
                condition.Render(mongo, "                ");
                var cosmos = new DocCosmosText("{p}");
                var text = condition.RenderCosmos(cosmos, "c");
                var writes = cosmos.Writes.Count == 0 ? "null" : "static (w, i, s) => { switch (i) { " + string.Concat(cosmos.Writes.Select((write, i) => $"case {i}: {write}; break; ")) + "} }";
                specifications.Add(new DocSpecificationModel(specifications.Count, location.GetInterceptsLocationAttributeSyntax(), entity.FullName, mongo.ToString(),
                    DocTypeEmitter.Literal(text), cosmos.Writes.Count, writes, string.Join(", ", captures)));
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.Translated, invocation.GetLocation(), $"Spec<{entityType.Name}>.Where", condition.Describe() + " | Cosmos DB: " + text));
            }
        }

        return specifications;
    }
}
