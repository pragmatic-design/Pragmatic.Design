using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     Flags reach-in across boundaries (PRAG0686): a type injecting another boundary's generated
///     <c>DbContext</c>. The owning boundary is read from the generated
///     <c>[PragmaticDbContext("&lt;boundary&gt;")]</c> attribute; if the consuming type's namespace does
///     not contain that boundary segment, it is reaching into a boundary it does not own. The fix is
///     to call that boundary's actions/queries or react to its events instead of its persistence.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossBoundaryDbContextAnalyzer : DiagnosticAnalyzer
{
    private const string DbContextAttributeMetadataName = "Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.CrossBoundaryDbContext);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var dbContextAttribute = start.Compilation.GetTypeByMetadataName(DbContextAttributeMetadataName);
            if (dbContextAttribute is null)
                return;

            start.RegisterSymbolAction(
                ctx => AnalyzeType(ctx, dbContextAttribute),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol dbContextAttribute)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return;

        var consumerNamespace = type.ContainingNamespace?.ToDisplayString() ?? "";

        // Constructor parameters (the DI injection vector) + fields typed as a boundary DbContext.
        var injectionTypes = type.InstanceConstructors
            .SelectMany(c => c.Parameters.Select(p => (Symbol: (ISymbol)p, p.Type, p.Locations)))
            .Concat(type.GetMembers().OfType<IFieldSymbol>()
                .Where(f => !f.IsImplicitlyDeclared)
                .Select(f => (Symbol: (ISymbol)f, f.Type, f.Locations)));

        foreach (var (_, candidateType, locations) in injectionTypes)
        {
            var boundary = GetOwningBoundary(candidateType, dbContextAttribute);
            if (boundary is null)
                continue;

            if (NamespaceBelongsToBoundary(consumerNamespace, boundary))
                continue;

            var location = locations.FirstOrDefault();
            if (location is null)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.CrossBoundaryDbContext,
                location,
                type.Name,
                candidateType.Name,
                boundary));
        }
    }

    /// <summary>
    ///     Returns the boundary name from the type's (or a base type's) [PragmaticDbContext("…")]
    ///     attribute, or null if the type is not a generated boundary DbContext.
    /// </summary>
    private static string? GetOwningBoundary(ITypeSymbol type, INamedTypeSymbol dbContextAttribute)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, dbContextAttribute))
                    continue;

                if (attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is string boundary
                    && !string.IsNullOrEmpty(boundary))
                    return boundary;
            }
        }

        return null;
    }

    private static bool NamespaceBelongsToBoundary(string ns, string boundary)
        => ns.Split('.').Any(segment => string.Equals(segment, boundary, System.StringComparison.Ordinal));
}
