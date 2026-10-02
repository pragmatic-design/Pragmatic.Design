using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Result.Analyzers;

/// <summary>
///     Reports <c>PRAG0002</c> when a type extends <c>Pragmatic.Result.Error</c> and declares its own
///     custom public properties but is <em>not</em> <c>partial</c>.
/// </summary>
/// <remarks>
///     The source generator emits the <c>WriteExtensions</c> override — the seam that surfaces an
///     error's custom properties in ProblemDetails and OpenAPI — only for error types declared
///     <c>partial</c> (the syntactic filter runs before semantic analysis). A non-partial error with
///     custom properties therefore compiles cleanly, yet those properties are silently absent from the
///     HTTP response. This is the module's worst silent failure, so the analyzer surfaces it.
///     It does not fire when the type already declares a manual <c>WriteExtensions</c> override — in
///     that case the properties reach the wire regardless of the generator.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ErrorPartialAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     Property names defined on <c>IError</c>/<c>Error</c> (plus the record-synthesized
    ///     <c>EqualityContract</c>). A property with one of these names is not a custom property.
    ///     Kept in sync with the source generator's own base-property set.
    /// </summary>
    private static readonly HashSet<string> BasePropertyNames = new(System.StringComparer.Ordinal)
    {
        "Code", "StatusCode", "Title", "Description", "MessageKey", "Parameters",
        "IsTransient", "RetryAfter", "TitleKey", "DescriptionKey", "EqualityContract",
    };

    private const string ErrorBaseFullName = "Pragmatic.Result.Error";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.MissingPartialOnErrorWithCustomProperties);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var symbol = (INamedTypeSymbol)context.Symbol;

        // class / record class → Class; struct / record struct → Struct.
        if (symbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return;

        // Must derive from Pragmatic.Result.Error.
        if (!ExtendsErrorBase(symbol))
            return;

        // If any declaration is already partial, the generator can contribute the override.
        if (IsPartial(symbol))
            return;

        // A manual WriteExtensions override already carries the properties to the wire — no risk.
        if (DeclaresWriteExtensions(symbol))
            return;

        // Only warn when the type actually declares a custom property that would be dropped.
        if (!HasCustomProperty(symbol))
            return;

        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? symbol.Locations[0];
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.MissingPartialOnErrorWithCustomProperties,
            location,
            symbol.Name));
    }

    private static bool ExtendsErrorBase(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == ErrorBaseFullName)
                return true;
            current = current.BaseType;
        }

        return false;
    }

    /// <summary>True when any of the type's declarations carries the <c>partial</c> modifier.</summary>
    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax typeDecl
                && typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;
        }

        return false;
    }

    /// <summary>True when a <c>WriteExtensions</c> method is declared directly on the type.</summary>
    private static bool DeclaresWriteExtensions(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers("WriteExtensions"))
        {
            if (member is IMethodSymbol)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     True when the type declares at least one public, non-static, non-indexer, readable property
    ///     of its own (not inherited, not a base/record-synthesized name).
    /// </summary>
    private static bool HasCustomProperty(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (prop.IsStatic || prop.IsIndexer)
                continue;
            if (prop.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (prop.GetMethod is null)
                continue;
            if (BasePropertyNames.Contains(prop.Name))
                continue;
            // Only properties declared on this type itself — not inherited overrides.
            if (!SymbolEqualityComparer.Default.Equals(prop.ContainingType, symbol))
                continue;

            return true;
        }

        return false;
    }
}
