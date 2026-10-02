using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     Heuristic DDD smell (PRAG0685, disabled by default): an <c>[Entity]</c> that owns more than
///     <see cref="Threshold"/> child collections (collection-typed navigations whose element is itself
///     an <c>[Entity]</c>) has a wide consistency boundary. Suggests splitting the aggregate or
///     referencing other aggregates by id. Opt in via <c>.editorconfig</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AggregateSizeAnalyzer : DiagnosticAnalyzer
{
    private const int Threshold = 5;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.AggregateTooLarge);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || !EntityFilter.IsTarget(type))
            return;

        var childCollections = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Count(p => !p.IsStatic && IsChildCollection(p.Type));

        if (childCollections <= Threshold)
            return;

        var location = type.Locations.FirstOrDefault();
        if (location is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.AggregateTooLarge,
            location,
            type.Name,
            childCollections));
    }

    /// <summary>
    ///     True when <paramref name="type"/> is a collection (not a string) whose element is an [Entity].
    /// </summary>
    private static bool IsChildCollection(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;

        var element = GetEnumerableElement(type);
        return element is not null && EntityFilter.IsTarget(element as INamedTypeSymbol);
    }

    private static ITypeSymbol? GetEnumerableElement(ITypeSymbol type)
    {
        // Arrays: T[] (entity arrays are unusual but handle them).
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        // IEnumerable<T> itself.
        if (type is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            return named.TypeArguments[0];

        // Any type implementing IEnumerable<T> (List<T>, ICollection<T>, HashSet<T>, …).
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                return iface.TypeArguments[0];
        }

        return null;
    }
}
