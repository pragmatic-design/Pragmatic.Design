using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Persistence.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EntityConstructorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.PreferEntityCreate);

    public override void Initialize(AnalysisContext context)
    {
        // Skip generated code — the Create() factory itself uses 'new'
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeObjectCreation,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var typeSymbol = GetConstructedType(context);
        if (typeSymbol is null)
            return;

        if (!EntityFilter.IsTarget(typeSymbol))
            return;

        // The factory has to build the entity somehow, and telling its author to call the factory is
        // advice they cannot take. Generated factories were already exempt through the generated-code
        // flag; a hand-written one is not generated code, so it took the warning for doing exactly what
        // the diagnostic asks for. The reference application never showed this because it does not load
        // this analyzer at all.
        if (IsInsideTheTypesOwnFactory(context, typeSymbol))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.PreferEntityCreate,
            context.Node.GetLocation(),
            typeSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    ///     Whether this <c>new</c> sits inside a static factory of the very type being constructed.
    /// </summary>
    /// <remarks>
    ///     Three conditions, all required: the enclosing method is static, it is declared on the
    ///     constructed type, and it hands that type back. A static helper on some other type that
    ///     happens to build an entity is still the case this diagnostic is for, and so is an instance
    ///     method — only the type's own factory is exempt.
    /// </remarks>
    private static bool IsInsideTheTypesOwnFactory(
        SyntaxNodeAnalysisContext context, INamedTypeSymbol constructed)
    {
        // A lambda inside the factory reports itself as the containing symbol, so walk out of any
        // anonymous functions before asking what method this is.
        var symbol = context.ContainingSymbol;
        while (symbol is IMethodSymbol { MethodKind: MethodKind.LambdaMethod or MethodKind.AnonymousFunction })
            symbol = symbol.ContainingSymbol;

        if (symbol is not IMethodSymbol { IsStatic: true } method)
            return false;

        if (!SymbolEqualityComparer.Default.Equals(
                method.ContainingType?.OriginalDefinition, constructed.OriginalDefinition))
            return false;

        return HandsBack(method.ReturnType, constructed);
    }

    /// <summary>
    ///     Whether a return type is the entity, or the entity wrapped in a task or a collection.
    /// </summary>
    private static bool HandsBack(ITypeSymbol returnType, INamedTypeSymbol constructed)
    {
        if (SymbolEqualityComparer.Default.Equals(returnType.OriginalDefinition, constructed.OriginalDefinition))
            return true;

        // Task<T>, ValueTask<T>, IReadOnlyList<T> — a factory that builds several, or builds
        // asynchronously, is still a factory.
        return returnType is INamedTypeSymbol { IsGenericType: true } generic
               && generic.TypeArguments.Length == 1
               && SymbolEqualityComparer.Default.Equals(
                   generic.TypeArguments[0].OriginalDefinition, constructed.OriginalDefinition);
    }

    /// <summary>
    ///     Resolves the type being constructed from either <c>new T()</c> or <c>new()</c> syntax.
    /// </summary>
    private static INamedTypeSymbol? GetConstructedType(SyntaxNodeAnalysisContext context)
    {
        switch (context.Node)
        {
            case ObjectCreationExpressionSyntax objectCreation:
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(objectCreation, context.CancellationToken);
                return typeInfo.Type as INamedTypeSymbol;
            }
            case ImplicitObjectCreationExpressionSyntax implicitCreation:
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(implicitCreation, context.CancellationToken);
                return typeInfo.Type as INamedTypeSymbol;
            }
            default:
                return null;
        }
    }
}
