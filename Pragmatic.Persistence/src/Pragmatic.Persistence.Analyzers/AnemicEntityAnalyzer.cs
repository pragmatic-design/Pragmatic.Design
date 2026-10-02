using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     Enforces anemic entities (Analyzer A): an <c>[Entity]</c> type should carry data, declarative invariants
///     and computed projections — not behavior. A user-declared method on an entity means behavior is leaking
///     in; it belongs in a mutation or domain action. Reports <c>PRAG0683</c>. Disabled by default — a codebase
///     migrates first, then opts in via <c>.editorconfig</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AnemicEntityAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.EntityShouldHaveNoBehavior);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeEntity, SymbolKind.NamedType);
    }

    private static void AnalyzeEntity(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!EntityFilter.IsTarget(type))
            return;

        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!IsUserBehaviorMethod(method))
                continue;

            var location = method.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.EntityShouldHaveNoBehavior, location, type.Name, method.Name));
        }
    }

    private static bool IsUserBehaviorMethod(IMethodSymbol method)
    {
        // Only ordinary methods — excludes constructors, operators, property/event accessors.
        if (method.MethodKind != MethodKind.Ordinary)
            return false;

        // Excludes compiler/record synthesized members and object overrides (ToString/Equals/...).
        if (method.IsImplicitlyDeclared || method.IsOverride)
            return false;

        // Records' Deconstruct and the value-equality contract are structure, not behavior.
        if (method.Name == "Deconstruct")
            return false;

        // Excludes SG-generated plumbing (setters, Create) which lives in *.g.cs partials.
        return method.DeclaringSyntaxReferences.Any(r => !IsGeneratedFile(r.SyntaxTree.FilePath));
    }

    private static bool IsGeneratedFile(string path) =>
        path.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".generated.cs", System.StringComparison.OrdinalIgnoreCase);
}
