using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     Blocks reflection-based entity instantiation that bypasses the generated
///     <c>Create()</c> factory: <c>Activator.CreateInstance&lt;Entity&gt;()</c>,
///     <c>Activator.CreateInstance(typeof(Entity))</c>,
///     <c>RuntimeHelpers.GetUninitializedObject(typeof(Entity))</c>, and
///     <c>FormatterServices.GetUninitializedObject(typeof(Entity))</c>.
///     Entity types must be created via the generated <c>Create()</c> factory to ensure
///     proper ID generation, audit timestamps, and default values.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotReflectEntityAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.DoNotReflectEntity);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        if (!IsReflectionInstantiationMethod(method))
            return;

        INamedTypeSymbol? entityType = null;

        // Case 1: generic type argument, e.g. Activator.CreateInstance<Entity>()
        if (method.IsGenericMethod && method.TypeArguments.Length == 1)
        {
            entityType = method.TypeArguments[0] as INamedTypeSymbol;
        }
        // Case 2: literal typeof(Entity) argument, e.g. Activator.CreateInstance(typeof(Entity)),
        //         RuntimeHelpers.GetUninitializedObject(typeof(Entity)),
        //         FormatterServices.GetUninitializedObject(typeof(Entity))
        else if (method.Parameters.Length >= 1 && invocation.ArgumentList?.Arguments.Count >= 1)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression;
            if (firstArg is TypeOfExpressionSyntax typeOfExpr)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(typeOfExpr.Type, context.CancellationToken);
                entityType = typeInfo.Type as INamedTypeSymbol;
            }
        }

        if (entityType is null || !EntityFilter.IsTarget(entityType))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.DoNotReflectEntity,
            invocation.GetLocation(),
            entityType.Name);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    ///     Returns true when the method is one of the known reflection-based instantiation APIs
    ///     that bypass an entity's generated <c>Create()</c> factory (and its constructor).
    /// </summary>
    private static bool IsReflectionInstantiationMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType?.ToDisplayString();

        return (containingType, method.Name) switch
        {
            // Activator.CreateInstance / CreateInstance<T>
            ("System.Activator", "CreateInstance") => true,
            // RuntimeHelpers.GetUninitializedObject(Type) — allocates without running any constructor
            ("System.Runtime.CompilerServices.RuntimeHelpers", "GetUninitializedObject") => true,
            // Legacy FormatterServices.GetUninitializedObject(Type)
            ("System.Runtime.Serialization.FormatterServices", "GetUninitializedObject") => true,
            _ => false
        };
    }
}
