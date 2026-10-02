using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator;

/// <summary>
///     Emits <c>[assembly: PendingContract("Type")]</c> for every endpoint whose operation body is still
///     <c>throw Behavior.Pending()</c> — not yet implemented. The contract-test generator reads these from the
///     compiled assembly to skip not-yet-implemented endpoints; once the body is implemented the metadata stops
///     being emitted and the contract test appears automatically.
/// </summary>
/// <remarks>
///     The call is resolved through the semantic model: matching the receiver's <em>text</em> against
///     "ends with Behavior" made any unrelated <c>FooBehavior.Pending()</c> of the user's declare the
///     endpoint unimplemented, and its contract test silently disappeared.
/// </remarks>
[Generator]
public sealed class PendingContractGenerator : IIncrementalGenerator
{
    private const string EndpointAttribute = "Pragmatic.Endpoints.Attributes.EndpointAttribute";
    private const string EndpointAttributeGeneric = "Pragmatic.Endpoints.Attributes.EndpointAttribute`1";
    private const string BehaviorTypeName = "Behavior";
    private const string BehaviorNamespace = "Pragmatic.Authoring";
    private const string PendingMethodName = "Pending";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var plain = context.SyntaxProvider
            .ForAttributeWithMetadataName(EndpointAttribute, static (n, _) => n is ClassDeclarationSyntax, Transform)
            .Collect();
        var generic = context.SyntaxProvider
            .ForAttributeWithMetadataName(EndpointAttributeGeneric, static (n, _) => n is ClassDeclarationSyntax, Transform)
            .Collect();

        context.RegisterSourceOutputSafe(plain.Combine(generic),
            static (ctx, pair) => Emit(ctx, pair.Left, pair.Right));
    }

    /// <summary>Returns the endpoint's fully-qualified name when its operation body is pending; null otherwise.</summary>
    private static string? Transform(GeneratorAttributeSyntaxContext context, System.Threading.CancellationToken ct)
    {
        if (context.TargetNode is not TypeDeclarationSyntax type)
            return null;

        return type.Members.OfType<MethodDeclarationSyntax>()
            .Any(m => IsPendingMethod(m, context.SemanticModel, ct))
            ? context.TargetSymbol.ToDisplayString()
            : null;
    }

    private static bool IsPendingMethod(
        MethodDeclarationSyntax method, SemanticModel semanticModel, System.Threading.CancellationToken ct)
    {
        if (method.ExpressionBody?.Expression is ThrowExpressionSyntax arrow &&
            IsBehaviorPending(arrow.Expression, semanticModel, ct))
            return true;

        return method.Body is { Statements.Count: 1 } body
            && body.Statements[0] is ThrowStatementSyntax { Expression: { } thrown }
            && IsBehaviorPending(thrown, semanticModel, ct);
    }

    /// <summary>True only for <c>Pragmatic.Authoring.Behavior.Pending(...)</c>, resolved semantically.</summary>
    private static bool IsBehaviorPending(
        ExpressionSyntax? expression, SemanticModel semanticModel, System.Threading.CancellationToken ct)
    {
        if (expression is not InvocationExpressionSyntax invocation)
            return false;

        var info = semanticModel.GetSymbolInfo(invocation, ct);
        if (info.Symbol is not IMethodSymbol method)
            method = info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault()!;

        if (method is null)
            return false;

        return method.Name == PendingMethodName
            && method.ContainingType is { } containing
            && containing.Name == BehaviorTypeName
            && containing.ContainingNamespace?.ToDisplayString() == BehaviorNamespace;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<string?> plain, ImmutableArray<string?> generic)
    {
        var pending = plain.Concat(generic)
            .Where(static n => n is not null)
            .Select(static n => n!)
            .Distinct()
            .OrderBy(static n => n, System.StringComparer.Ordinal)
            .ToList();
        if (pending.Count == 0)
            return;

        var artifact = new PendingContractsTemplate(pending).RenderOutput();
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }
}
