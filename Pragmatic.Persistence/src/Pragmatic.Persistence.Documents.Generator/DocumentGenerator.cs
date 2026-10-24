using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.Persistence.Documents.Generator.Models;
using Pragmatic.Persistence.Documents.Generator.Templates;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Documents.Generator;

/// <summary>
///     The document generator: for each [Document] and [Embedded] partial class of the compilation, its reader and writer
///     (<see cref="DocTypeEmitter" />); for each call on a <c>DocQuery&lt;T&gt;</c>, an interceptor: an operator stores its argument in a slot,
///     a terminal runs a plan (<see cref="DocPlanner" />) built from the chain. What it cannot translate is a PRAGD diagnostic at the call site.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class DocumentGenerator : IIncrementalGenerator
{
    private const string Query = "global::Pragmatic.Persistence.Documents.DocQuery";
    private const string Execution = "global::Pragmatic.Persistence.Documents.DocExecution";

    /// <summary>Fully qualified with the nullable annotations: an interceptor's parameters repeat the intercepted method's exactly.</summary>
    private static readonly SymbolDisplayFormat Annotated = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var documents = context.SyntaxProvider.ForAttributeWithMetadataName(DocModel.DocumentAttribute, static (node, _) => node is ClassDeclarationSyntax, static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);
        var embedded = context.SyntaxProvider.ForAttributeWithMetadataName(DocModel.EmbeddedAttribute, static (node, _) => node is ClassDeclarationSyntax, static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);
        context.RegisterSourceOutput(documents.Collect().Combine(embedded.Collect()), static (spc, types) => EmitTypes(spc, types.Left.Concat(types.Right)));

        var calls = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax },
            static (ctx, ct) => DocCall.TryCreate(ctx, ct));
        // Pragmatic's entities (their shape, families, repositories and specifications) join with the unified generator, not here.
        context.RegisterSourceOutput(calls.Where(static c => c is not null).Collect(), static (spc, found) => EmitInterceptors(spc, found!));
    }

    private static void EmitTypes(SourceProductionContext spc, IEnumerable<INamedTypeSymbol> types)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var type in types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).OrderBy(t => t.ToDisplayString(), StringComparer.Ordinal))
        {
            if (DocModel.IsPragmatic(type))
                continue; // a Pragmatic entity with a [Document] is read with its shape, which this standalone generator does not emit
            var model = DocModel.Read(type, diagnostics);
            if (model is not null && DocTypeEmitter.Model(model, type.ToDisplayString().Replace('<', '_').Replace('>', '_') + ".Document.g.cs", diagnostics) is { } typeModel)
                spc.AddSource(new Templates.DocumentTypeTemplate(typeModel).RenderOutput());
        }

        foreach (var diagnostic in diagnostics)
            spc.ReportDiagnostic(diagnostic);
    }

    /// <summary>The chains' interceptors.</summary>
    private static void EmitInterceptors(SourceProductionContext spc, ImmutableArray<DocCall> calls)
    {
        if (calls.IsEmpty)
            return;
        var diagnostics = new List<Diagnostic>();
        var models = new Dictionary<INamedTypeSymbol, DocModel?>(SymbolEqualityComparer.Default);
        DocModel? Model(INamedTypeSymbol type)
        {
            if (!models.TryGetValue(type, out var model))
                models[type] = model = DocModel.Read(type, new List<Diagnostic>()); // a type's own problems are reported where it is declared
            return model;
        }

        var interceptors = new List<DocumentInterceptorModel>();
        foreach (var call in calls.OrderBy(c => c.Invocation.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(c => c.Invocation.SpanStart))
        {
            if (call.Entity is null)
            {
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.NotRooted, call.Invocation.GetLocation(), call.Name));
                continue;
            }

            var entity = Model(call.Entity);
            if (entity is null || entity.IsEmbedded)
            {
                diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, call.Invocation.GetLocation(), call.Name, $"{call.Entity.Name} is not a readable [Document] (see the diagnostics on the type)"));
                continue;
            }

            var location = call.Model.GetInterceptableLocation(call.Invocation);
            if (location is null)
                continue;
            var attribute = location.GetInterceptsLocationAttributeSyntax();
            var index = interceptors.Count;
            if ((call.IsTerminal ? Terminal(call, entity, Model, index, attribute, diagnostics) : Operator(call, entity, index, attribute, diagnostics)) is { } interceptor)
                interceptors.Add(interceptor);
        }

        spc.AddSource(new DocumentInterceptorsTemplate(interceptors.ToEquatableArray()).RenderOutput());
        foreach (var diagnostic in diagnostics.GroupBy(d => (d.Id, d.Location.SourceSpan, d.Location.SourceTree?.FilePath)).Select(g => g.First()))
            spc.ReportDiagnostic(diagnostic);
    }

    private static DocumentInterceptorModel? Operator(DocCall call, DocModel entity, int index, string attribute, List<Diagnostic> diagnostics)
    {
        var name = call.Name;
        var slot = call.NextSlot;
        var overGroups = call.Before.Any(s => s.Name == "GroupBy") && call.Before.Any(s => s.Name == "Select") && name is "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending";
        if (call.Before.Any(s => s.Name is "Select" or "SelectMany" or "Join") && name != "Select" && !overGroups)
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, call.Invocation.GetLocation(), name, "after a Select or a SelectMany only Skip, Take and the terminals: put the filter and the ordering before it"));
            return null;
        }

        var e = entity.FullName;
        var method = call.Method;
        DocumentInterceptorModel Method(string returns, string typeParameters, string parameters, string body) => new(attribute, returns, name, index, typeParameters, parameters, body);
        if (name == "Select")
            return Method($"{Query}<TResult>", "<TSource, TResult>", $"this in {Query}<TSource> q, global::System.Func<TSource, TResult> selector", $"q.WithSlot({slot}, selector).As<TResult>()");
        if (name == "Join")
        {
            // The plan reads the lambdas and the inner chain's source; the chain stores the inner chain (its own slots), the two keys and the result.
            return Method($"{Query}<TResult>", "<TOuter, TInner, TKey, TResult>",
                $"this in {Query}<TOuter> q, {Query}<TInner> inner, global::System.Func<TOuter, TKey> outerKey, global::System.Func<TInner, TKey> innerKey, global::System.Func<TOuter, TInner, TResult> result",
                $"q.WithSlot({slot}, inner).WithSlot({slot + 1}, outerKey).WithSlot({slot + 2}, innerKey).WithSlot({slot + 3}, result).As<TResult>()");
        }

        if (name == "SelectMany")
        {
            // The list is read by the plan from the lambda; the chain only takes the element type, for its terminal.
            return Method($"{Query}<TElement>", "<TSource, TElement>", $"this in {Query}<TSource> q, global::System.Func<TSource, global::System.Collections.Generic.IEnumerable<TElement>> selector", "q.As<TElement>()");
        }

        if (name == "GroupBy")
        {
            // The key is read by the plan from the lambda; the chain only takes the grouped element type, for its Select.
            return Method($"{Query}<global::System.Linq.IGrouping<TKey, TSource>>", "<TSource, TKey>", $"this in {Query}<TSource> q, global::System.Func<TSource, TKey> key", "q.As<global::System.Linq.IGrouping<TKey, TSource>>()");
        }

        if (overGroups)
        {
            // After a grouped Select the element is its anonymous type: generic in it; the plan reads the lambda, the chain stores an argument.
            return name != "Where"
                ? Method($"{Query}<T>", "<T, TKey>", $"this in {Query}<T> q, global::System.Func<T, TKey> key", "q")
                : method.OriginalDefinition.TypeParameters.Any(t => t.Name == "TArg")
                    ? Method($"{Query}<T>", "<T, TArg>", $"this in {Query}<T> q, global::System.Func<T, TArg, bool> predicate, TArg arg", $"q.WithSlot({slot}, arg)")
                    : Method($"{Query}<T>", "<T>", $"this in {Query}<T> q, global::System.Func<T, bool> predicate", "q");
        }

        if (method.TypeArguments.Any(t => t.IsAnonymousType))
        {
            diagnostics.Add(Diagnostic.Create(DocDiagnostics.Unsupported, call.Invocation.GetLocation(), name, "an anonymous type as a key, an argument or a cursor: use a field, a named type or a tuple"));
            return null;
        }

        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString(Annotated)} {p.Name}"));
        var stores = name == "After" ? $"q.WithSlot({slot}, cursor)" : name == "Matching" ? $"q.WithSlot({slot}, predicate)" : name == "OrderedBy" ? $"q.WithSlot({slot}, ordering)" : method.OriginalDefinition.TypeParameters.Any(t => t.Name == "TArg") ? $"q.WithSlot({slot}, arg)" : "q";
        return Method($"{Query}<{e}>", "", $"this in {Query}<{e}> q, {parameters}", stores);
    }

    private static DocumentInterceptorModel? Terminal(DocCall call, DocModel entity, Func<INamedTypeSymbol, DocModel?> models, int index, string attribute, List<Diagnostic> diagnostics)
    {
        var planner = new DocPlanner(call, entity, models, diagnostics);
        if (!planner.Build())
            return null;
        var name = call.Name;
        var method = call.Method;
        var where = call.Invocation.GetLocation().GetLineSpan();
        var origin = $"{Path.GetFileName(where.Path)}({where.StartLinePosition.Line + 1},{where.StartLinePosition.Character + 1})";
        var generic = call.Element.IsAnonymousType || call.Element.ToDisplayString().Contains("<anonymous");
        var element = generic ? "TElement" : call.Element.ToDisplayString(Annotated);
        var e = entity.FullName;

        // The parameters after the chain, as the intercepted method has them (the element's name replaced when it has none).
        var parameters = string.Join("", method.Parameters.Select(p => ", " + (generic ? "global::System.Threading.CancellationToken" : p.Type.ToDisplayString(Annotated)) + " " + p.Name));
        var typeParameters = generic ? "<TElement>" : "";
        var receiver = $"this in {Query}<{element}> q";
        var query = method.OriginalDefinition.TypeParameters.Any(t => t.Name == "TArg") ? $"q.WithSlot({planner.TerminalSlot}, arg)" : "q";
        var returns = method.ReturnType.ToDisplayString(Annotated);
        string invocation;
        switch (name)
        {
            case "ToListAsync":
                returns = $"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.List<{element}>>";
                invocation = planner.JoinInitializer is not null
                    ? $"{Execution}.JoinListAsync<{e}, {planner.JoinInner}, {planner.JoinKey}, {element}>({query}, Plan_{index}, Join_{index}, ct)"
                    : $"{Execution}.{(planner.Grouped ? "GroupListAsync" : planner.Unwound ? "UnwindListAsync" : "ToListAsync")}<{e}, {element}>({query}, Plan_{index}, ct)";
                break;
            case "FirstOrDefaultAsync":
                returns = $"global::System.Threading.Tasks.ValueTask<{element}{(generic || !call.Element.IsValueType ? (element.EndsWith("?", StringComparison.Ordinal) ? "" : "?") : "")}>";
                invocation = $"{Execution}.FirstOrDefaultAsync<{e}, {element}>({query}, Plan_{index}, ct)";
                break;
            case "CountAsync":
                invocation = $"{Execution}.CountAsync<{e}, {element}>({query}, Plan_{index}, ct)";
                break;
            case "AnyAsync":
                invocation = $"{Execution}.AnyAsync<{e}, {element}>({query}, Plan_{index}, ct)";
                break;
            case "ExecuteDeleteAsync" or "ExecuteUpdateAsync":
                invocation = $"{Execution}.{name}<{e}, {element}>({query}, Plan_{index}, ct)";
                break;
            default: // MaxAsync, MinAsync, SumAsync
                var value = method.TypeArguments[0].ToDisplayString(Annotated);
                var accumulator = name == "MaxAsync" ? "$max" : name == "MinAsync" ? "$min" : "$sum";
                invocation = $"{Execution}.AggregateAsync<{e}, {element}, {value}>({query}, Plan_{index}, \"{accumulator}\", ct)";
                break;
        }

        diagnostics.Add(Diagnostic.Create(DocDiagnostics.Translated, call.Invocation.GetLocation(), name, planner.Describe()));
        return new DocumentInterceptorModel(attribute, returns, name, index, typeParameters, receiver + parameters, invocation, e, planner.Initializer(origin), planner.JoinInitializer is null ? null : planner.JoinInner, planner.JoinInitializer);
    }
}
