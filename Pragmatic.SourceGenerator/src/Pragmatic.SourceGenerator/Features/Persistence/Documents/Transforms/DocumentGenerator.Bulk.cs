using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     Pragmatic's bulk update under the documents engine (the Sql engine's <c>SqlInterceptorGenerator.Bulk</c>, for documents): every
///     <c>IBulkRepository&lt;T&gt;.BulkUpdateAsync</c> written on a Pragmatic entity the documents engine serves is compiled where it is written, into
///     one plan: the documents the specification selects (a predicate composed at runtime, as <c>Matching</c>'s, the caller's families joined to
///     it) and the setters' lambda read into the update (<see cref="DocSetWriter" />), each value written from a slot the delegate's recording
///     fills, a value computed from the document translated (<see cref="DocComputedTranslator" />). The call is intercepted to run that plan
///     through <c>DocBulk</c>, on the session of the repository's unit of work. The delete, the insert and the upsert are the repository's own.
/// </summary>
internal static partial class DocumentGenerator
{
    private const string BulkRepository = "Pragmatic.Persistence.Repository.IBulkRepository`1";

    private static void BulkUpdates(Compilation compilation, Func<INamedTypeSymbol, DocModel?> models, List<DocumentInterceptorModel> interceptors, List<Diagnostic> diagnostics)
    {
        if (compilation.GetTypeByMetadataName("Pragmatic.Persistence.Documents.DocBulk") is null || compilation.GetTypeByMetadataName(BulkRepository) is not { } bulk)
            return;
        var engines = PragmaticEngines.Of(compilation);
        foreach (var tree in compilation.SyntaxTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "BulkUpdateAsync" }
                    || model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
                    || SqlInterceptorGenerator.BulkEntity(method, bulk) is not { } entityType
                    || !DocModel.IsPragmatic(entityType)
                    || !engines.Serves(entityType, PragmaticEngine.Documents) // another engine's entity: its own repository runs the call
                    || models(entityType) is not { } entity
                    || model.GetInterceptableLocation(invocation) is not { } location)
                    continue;
                if (invocation.ArgumentList.Arguments.Count < 2 || invocation.ArgumentList.Arguments[1].Expression is not LambdaExpressionSyntax setters)
                {
                    diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, invocation.GetLocation(), "BulkUpdateAsync", "the setters are a lambda written at the call: s => s.SetProperty(o => o.Field, value), the update read from it"));
                    continue;
                }

                if (BulkSets(model, entity, models, setters, diagnostics) is not { } update)
                    continue;
                var index = interceptors.Count;
                var type = entity.FullName;
                var span = invocation.GetLocation().GetLineSpan();
                var origin = $"{Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
                var cosmos = DocCosmosText.Select("*", null, [], new DocCosmosText(), families: true); // the documents to write by id, the predicate and the families resolved per call
                var patch = update.Writer.Cosmos() is { } operations ? $"static (w, e, s) => {{ {operations}}}" : "null";
                var plan = $"new global::Pragmatic.Persistence.Documents.Planning.DocPlan<{type}>({DocTypeEmitter.Literal(origin)}, null, null, null, "
                           + $"cosmos: new global::Pragmatic.Persistence.Documents.Planning.DocCosmosQuery({DocTypeEmitter.Literal(cosmos)}, null, null, false, null, 0), "
                           + $"update: static (w, s) => {{ {update.Writer.Mongo()}}}, predicateSlot: 0, cosmosUpdate: {patch}, updatePipeline: {(update.Writer.Pipeline ? "true" : "false")})";
                var chain = $"session.Query<{type}>().WithSlot(0, predicate)" + string.Concat(Enumerable.Range(0, update.Values).Select(i => $".WithSlot({i + 1}, values[{i}])"));
                var run = $"static (session, predicate, values, ct) => {Execution}.ExecuteUpdateAsync<{type}, {type}>({chain}, Plan_{index}, ct)";
                var receiver = $"this {method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} repository";
                var parameters = string.Concat(method.Parameters.Select(p => $", {p.Type.ToDisplayString(Annotated)} {p.Name}{(p.HasExplicitDefaultValue ? " = default" : "")}"));
                interceptors.Add(new DocumentInterceptorModel(location.GetInterceptsLocationAttributeSyntax(), "global::System.Threading.Tasks.Task<int>", "BulkUpdateAsync", index, "", receiver + parameters,
                    $"global::Pragmatic.Persistence.Documents.DocBulk.UpdateAsync(repository, filter, set, {run}, ct)", type, plan));
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.Translated, invocation.GetLocation(), $"BulkUpdateAsync on {entityType.Name}", (update.Writer.Pipeline ? "an update pipeline" : "an update") + " | Cosmos DB: " + cosmos));
            }
        }
    }

    /// <summary>
    ///     The setters' lambda (<c>s =&gt; s.SetProperty(o =&gt; o.Field, value).SetProperty(o =&gt; o.Other, o =&gt; o.Other + 1)</c>) as the update's
    ///     sets: a value form's value from the slot after the predicate's, in the order of the calls (what <c>BulkSetterValues</c> records); null when
    ///     something was reported.
    /// </summary>
    private static (DocSetWriter Writer, int Values)? BulkSets(SemanticModel model, DocModel entity, Func<INamedTypeSymbol, DocModel?> models, LambdaExpressionSyntax setters, List<Diagnostic> diagnostics)
    {
        var calls = new List<InvocationExpressionSyntax>();
        var current = setters.Body as ExpressionSyntax;
        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "SetProperty" } access, ArgumentList.Arguments.Count: 2 } call)
        {
            calls.Insert(0, call); // s.SetProperty(a).SetProperty(b) is SetProperty(b) on SetProperty(a) on s
            current = access.Expression;
        }

        var parameter = setters switch
        {
            SimpleLambdaExpressionSyntax simple => model.GetDeclaredSymbol(simple.Parameter),
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => model.GetDeclaredSymbol(parenthesized.ParameterList.Parameters[0]),
            _ => null,
        };
        if (calls.Count == 0 || current is not IdentifierNameSyntax root || parameter is null || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(root).Symbol, parameter))
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, setters.GetLocation(), "BulkUpdateAsync", "the setters are calls of SetProperty on the lambda's parameter: s => s.SetProperty(o => o.Field, value).SetProperty(o => o.Other, value)"));
            return null;
        }

        var writer = new DocSetWriter(entity);
        var values = 0;
        foreach (var call in calls)
        {
            if (call.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax { Body: ExpressionSyntax fieldBody } fieldLambda || model.GetDeclaredSymbol(fieldLambda.Parameter) is not IParameterSymbol row)
            {
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, call.GetLocation(), "SetProperty", "its first argument selects a field: o => o.Status"));
                return null;
            }

            var translator = new DocTranslator(model, entity, models, row, null, null, diagnostics, call.GetLocation());
            var path = translator.Path(fieldBody, out var field);
            if (path is null || field is null)
            {
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, call.GetLocation(), "SetProperty", $"'{fieldBody}' is not a field of the document"));
                return null;
            }

            if (field.Kind is DocKind.Embedded or DocKind.List || field == entity.Key || field == entity.Version || field == entity.PartitionKey)
            {
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, call.GetLocation(), "SetProperty", $"it writes a value field: not the key, the version, the partition key, an embedded document or an array ({field.Property.Name})"));
                return null;
            }

            if (model.GetSymbolInfo(call).Symbol is IMethodSymbol { Parameters.Length: 2 } setProperty && setProperty.Parameters[1].Type.Name == "Expression")
            {
                if (call.ArgumentList.Arguments[1].Expression is not SimpleLambdaExpressionSyntax { Body: ExpressionSyntax computedBody } computedLambda || model.GetDeclaredSymbol(computedLambda.Parameter) is not IParameterSymbol document)
                {
                    diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, call.GetLocation(), "SetProperty", "a computed value is a lambda written at the call: o => o.Stars + 1"));
                    return null;
                }

                if (new DocComputedTranslator(model, entity, models, document, diagnostics).Translate(computedBody, field) is not { } computed)
                    return null;
                writer.Computed(path, computed);
                continue;
            }

            // The value is the delegate's, whatever it captures: a slot filled from what it records.
            values++;
            writer.Value(path, BulkValue(field, values));
        }

        return (writer, values);
    }

    /// <summary>A recorded value as the update writes it: the slot cast to the property's type, an enum as its Int32.</summary>
    private static DocValue BulkValue(DocField field, int slot)
    {
        var type = field.Property.Type;
        var code = $"(({type.ToDisplayString(DocModel.Qualified)})s[{slot}]!)";
        if (field.Kind == DocKind.Enum)
            code = (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } ? "(int?)" : "(int)") + code;
        return new DocValue(code);
    }
}
