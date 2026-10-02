using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Abstractions.Analyzers;

/// <summary>
///     Warns when <c>[Inject]</c> is applied without <c>Required = true</c> (PRAG1452).
///     The default <c>Required = false</c> injects a missing service as <see langword="null"/>,
///     which can hide a misconfiguration and cause a <c>NullReferenceException</c> at first use.
///     The id sits in <c>PRAG1400-1499</c>, the range of the assembly that emits it, not
///     the range of its subject (<c>[Inject]</c> belongs to Composition): an id picked by subject
///     collides with the source generator's own ids in that range.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InjectRequiredAnalyzer : DiagnosticAnalyzer
{
    private const string InjectAttributeMetadataName = "Pragmatic.Composition.Attributes.InjectAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.OptionalInjection);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var injectAttributeType = compilationContext.Compilation
                .GetTypeByMetadataName(InjectAttributeMetadataName);

            // The attribute is not referenced in this compilation — nothing to analyze.
            if (injectAttributeType is null)
                return;

            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeSymbol(symbolContext, injectAttributeType),
                SymbolKind.Property,
                SymbolKind.Method);
        });
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol injectAttributeType)
    {
        var symbol = context.Symbol;

        var injectAttribute = FindInjectAttribute(symbol, injectAttributeType);
        if (injectAttribute is null)
            return;

        // Required = true → fail-fast opt-in, no diagnostic.
        if (IsRequiredTrue(injectAttribute))
            return;

        var location = GetAttributeLocation(injectAttribute) ?? symbol.Locations.FirstOrDefault();
        if (location is null)
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.OptionalInjection,
            location,
            symbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static AttributeData? FindInjectAttribute(ISymbol symbol, INamedTypeSymbol injectAttributeType)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, injectAttributeType))
                return attribute;
        }

        return null;
    }

    /// <summary>
    ///     Returns <see langword="true"/> only when the <c>Required</c> named argument is explicitly
    ///     set to the boolean literal <c>true</c>. Absence of the argument means the default
    ///     (<c>false</c>) and therefore the optional/null path.
    /// </summary>
    private static bool IsRequiredTrue(AttributeData injectAttribute)
    {
        foreach (var namedArgument in injectAttribute.NamedArguments)
        {
            if (namedArgument.Key != "Required")
                continue;

            return namedArgument.Value is { Kind: TypedConstantKind.Primitive, Value: true };
        }

        return false;
    }

    private static Location? GetAttributeLocation(AttributeData attribute)
        => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
}
