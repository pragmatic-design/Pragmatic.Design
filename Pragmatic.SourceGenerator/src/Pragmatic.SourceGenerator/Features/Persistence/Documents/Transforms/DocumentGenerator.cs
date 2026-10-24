using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;
using ShapeSourceModel = Pragmatic.SourceGenerator.Features.Persistence.Models.ShapeSourceModel;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     The documents engine's generation: for each [Document] and [Embedded] partial class of the compilation, its reader and writer
///     (<see cref="DocTypeEmitter" />); for each call on a <c>DocQuery&lt;T&gt;</c>, an interceptor: an operator stores its argument in a slot,
///     a terminal runs a plan (<see cref="DocPlanner" />) built from the chain. What it cannot translate is a PRAG31xx diagnostic at the call site.
/// </summary>
internal static partial class DocumentGenerator
{
    private const string Query = "global::Pragmatic.Persistence.Documents.DocQuery";
    private const string Execution = "global::Pragmatic.Persistence.Documents.Execution.DocExecution";
    private const string Session = "Pragmatic.Persistence.Documents.DocSession";

    /// <summary>Fully qualified with the nullable annotations: an interceptor's parameters repeat the intercepted method's exactly.</summary>
    private static readonly SymbolDisplayFormat Annotated = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>True when the compilation references the documents engine's runtime: what the call sites check before binding.</summary>
    private static bool UsesDocuments(Compilation compilation) => compilation.GetTypeByMetadataName(Session) is not null;

    /// <summary>The documents engine's generation, registered by <c>PragmaticSourceGenerator</c>.</summary>
    /// <param name="context">The generator's context.</param>
    /// <param name="shapeSources">The entities' shape files as Persistence emits them: the members a chain over a Pragmatic entity is bound against.</param>
    internal static void Register(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<EquatableArray<ShapeSourceModel>> shapeSources)
    {
        var documents = context.SyntaxProvider.ForAttributeWithMetadataName(DocModel.DocumentAttribute, static (node, _) => node is ClassDeclarationSyntax, static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);
        var embedded = context.SyntaxProvider.ForAttributeWithMetadataName(DocModel.EmbeddedAttribute, static (node, _) => node is ClassDeclarationSyntax, static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);
        context.RegisterSourceOutput(documents.Collect().Combine(embedded.Collect()), static (spc, types) => EmitTypes(spc, types.Left.Concat(types.Right)));

        // Every member call of every file reaches the transform: the binding is skipped where the compilation has no documents engine.
        var calls = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax },
            static (ctx, ct) => UsesDocuments(ctx.SemanticModel.Compilation) ? DocCall.TryCreate(ctx, ct) : null);
        // With Pragmatic's entities the chains are bound again on a compilation that has their shape (DocumentGenerator.Pragmatic).
        var combined = calls.Where(static c => c is not null).Collect().Combine(context.CompilationProvider).Combine(shapeSources);
        context.RegisterSourceOutput(combined, static (spc, found) =>
        {
            var ((sites, compilation), shape) = found;
            var shaped = UsesDocuments(compilation) ? WithPragmaticShape(spc, compilation, shape) : null;
            EmitInterceptors(spc, shaped is not { } s ? sites! : BindAgain(s.Written, s.Shadow, spc.CancellationToken), shaped?.Shadow);
        });
    }

    private static void EmitTypes(SourceProductionContext spc, IEnumerable<INamedTypeSymbol> types)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var type in types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).OrderBy(t => t.ToDisplayString(), StringComparer.Ordinal))
        {
            if (DocModel.IsPragmatic(type))
                continue; // a Pragmatic entity with a [Document] is read with its shape (WithPragmaticShape)
            var model = DocModel.Read(type, diagnostics);
            if (model is not null && DocTypeEmitter.Model(model, VirtualFolderHints.ForType(type.Name, "Document", type.ContainingNamespace.ToDisplayString()), diagnostics) is { } typeModel)
                spc.AddSource(new DocumentTypeTemplate(typeModel).RenderOutput());
        }

        foreach (var diagnostic in diagnostics)
            spc.ReportDiagnostic(diagnostic);
    }

    /// <summary>The chains' interceptors; with <paramref name="shadow" /> (Pragmatic's entities, their shape), the specifications' too.</summary>
    private static void EmitInterceptors(SourceProductionContext spc, ImmutableArray<DocCall> calls, Compilation? shadow)
    {
        if (calls.IsEmpty && shadow is null)
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

        var specifications = shadow is null ? [] : Specifications(shadow, Model, diagnostics);
        spc.AddSource(new DocumentInterceptorsTemplate(interceptors.ToEquatableArray(), specifications.ToEquatableArray()).RenderOutput());
        // A diagnostic in a file this generator added (a repository's chain) has no place in the compilation: a note is dropped, a failure kept unplaced.
        var generated = shadow is null ? null : new HashSet<string>(shadow.SyntaxTrees.Where(t => t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal)).Select(t => t.FilePath));
        foreach (var diagnostic in diagnostics.GroupBy(d => (d.Id, d.Location.SourceSpan, d.Location.SourceTree?.FilePath)).Select(g => g.First()))
        {
            if (generated is not null && diagnostic.Location.SourceTree?.FilePath is { } path && generated.Contains(path))
            {
                if (diagnostic.Severity >= DiagnosticSeverity.Warning)
                    spc.ReportDiagnostic(Diagnostic.Create(DocDiagnostics.Unsupported, Location.None, path, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
                continue;
            }

            spc.ReportDiagnostic(diagnostic);
        }
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
