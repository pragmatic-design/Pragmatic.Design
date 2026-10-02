using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Pragmatic.Abstractions.Analyzers;

/// <summary>
///     Flags calls to <c>IServiceCollection.BuildServiceProvider()</c> (PRAG1451). Building a
///     provider during configuration creates a second container distinct from the host's: singletons
///     and options are duplicated and disposables leak. Resolve from the app's provider instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BuildServiceProviderAnalyzer : DiagnosticAnalyzer
{
    private const string MethodName = "BuildServiceProvider";
    private const string ExtensionsType = "Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.BuildServiceProvider);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name != MethodName)
            return;

        if (method.ContainingType?.ToDisplayString() != ExtensionsType)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.BuildServiceProvider,
            invocation.Syntax.GetLocation()));
    }
}
