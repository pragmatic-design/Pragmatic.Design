using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Reports a generated boundary-actions facade injected where the caller is not trusted
///     (<c>PRAG0441</c>).
/// </summary>
/// <remarks>
///     <para>
///         The facade is recognised by the attribute the generator puts on it,
///         <c>[BoundaryActions&lt;TBoundary&gt;]</c>, rather than by a name pattern: the interface is
///         named after the consumer's boundary, so there is nothing stable to match on.
///     </para>
///     <para>
///         Trusted callers are the ones whose own entry point has already checked a permission, or
///         that have no user to check: an action, a mutation, an event or message handler, a job. The
///         generated implementations inject each other and are excluded as generated code, not by a
///         rule here.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BoundaryActionsInjectionAnalyzer : DiagnosticAnalyzer
{
    private const string FacadeAttribute = "Pragmatic.Actions.Attributes.BoundaryActionsAttribute`1";

    /// <summary>
    ///     Attributes whose presence means the entry point already ran the authorization pipeline, or
    ///     that there is no request-bound caller to authorize.
    /// </summary>
    private static readonly ImmutableHashSet<string> TrustedAttributes = ImmutableHashSet.Create(
        "DomainActionAttribute",
        "MutationAttribute",
        "CompositeActionAttribute",
        "EventHandlerAttribute",
        "JobAttribute",
        "RecurringJobAttribute");

    /// <summary>
    ///     Handler interfaces the dispatcher already invokes as an internal call. Resolved to symbols
    ///     rather than compared as strings: a metadata name carries a generic arity and a display
    ///     string does not, and getting that wrong makes the exemption silently never match.
    /// </summary>
    private static readonly string[] TrustedInterfaceNames =
    [
        "Pragmatic.Messaging.IMessageHandler`1",
        "Pragmatic.Events.IDomainEventHandler`1",
        "Pragmatic.Abstractions.Events.IDomainEventHandler`1"
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(BoundaryActionsInjectionDescriptors.Prag0441);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // No Actions in the compilation means no facade to be injected.
        var marker = context.Compilation.GetTypeByMetadataName(FacadeAttribute);
        if (marker is null)
            return;

        var handlers = TrustedInterfaceNames
            .Select(context.Compilation.GetTypeByMetadataName)
            .Where(t => t is not null)
            .ToImmutableArray();

        context.RegisterSymbolAction(c => Analyze(c, marker, handlers),
            SymbolKind.Field, SymbolKind.Property, SymbolKind.Parameter);
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol marker,
        ImmutableArray<INamedTypeSymbol?> handlers)
    {
        var (type, containing) = context.Symbol switch
        {
            IFieldSymbol f => ((ITypeSymbol?)f.Type, f.ContainingType),
            IPropertySymbol p => (p.Type, p.ContainingType),
            IParameterSymbol { ContainingSymbol: IMethodSymbol m } p => (p.Type, m.ContainingType),
            _ => (null, null)
        };

        if (type is null || containing is null)
            return;

        if (!IsFacade(type, marker) || IsTrusted(containing, marker, handlers))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            BoundaryActionsInjectionDescriptors.Prag0441,
            context.Symbol.Locations.FirstOrDefault(),
            type.Name,
            containing.Name));
    }

    private static bool IsFacade(ITypeSymbol type, INamedTypeSymbol marker)
    {
        if (type.TypeKind != TypeKind.Interface)
            return false;

        return HasMarker(type, marker);
    }

    private static bool HasMarker(ITypeSymbol type, INamedTypeSymbol marker)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass?.OriginalDefinition;
            if (attributeClass is not null &&
                SymbolEqualityComparer.Default.Equals(attributeClass, marker))
                return true;
        }

        return false;
    }

    private static bool IsTrusted(
        INamedTypeSymbol containing,
        INamedTypeSymbol marker,
        ImmutableArray<INamedTypeSymbol?> handlers)
    {
        foreach (var attribute in containing.GetAttributes())
            if (attribute.AttributeClass is { } a && TrustedAttributes.Contains(a.Name))
                return true;

        foreach (var iface in containing.AllInterfaces)
        {
            var definition = iface.OriginalDefinition;

            foreach (var handler in handlers)
                if (SymbolEqualityComparer.Default.Equals(definition, handler))
                    return true;

            // A facade implementing another facade is the generator's own composition, reached only
            // where generated-code exclusion does not apply.
            if (HasMarker(definition, marker))
                return true;
        }

        return HasMarker(containing, marker);
    }
}
