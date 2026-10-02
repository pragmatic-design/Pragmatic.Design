using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Companion analyzer for SG-emitted "must be partial" diagnostics.
///     Detects classes decorated with Pragmatic attributes that are not declared as partial,
///     enabling IDE code fixes to add the <c>partial</c> modifier.
/// </summary>
/// <remarks>
///     <para>
///         This analyzer is the <b>only</b> source of every ID it supports. It travels in the
///         generator's package, so it runs wherever the generator does, and it reports on the type's
///         declaration — where "Make class partial" can act. The generator skips a non-partial type
///         without reporting it. Do not re-add a copy to the generator: two descriptors with one ID are
///         one diagnostic to Roslyn, reported twice to the user and suppressed together (the same
///         treatment as PRAG0600, PRAG0602 and PRAG1100).
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NotPartialClassAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     Maps Pragmatic attribute names to their corresponding "must be partial" diagnostic descriptor.
    ///     Key = attribute class name (without "Attribute" suffix, as it appears in source).
    ///     The analyzer checks the attribute's containing namespace to confirm it's a Pragmatic attribute.
    /// </summary>
    private static readonly Dictionary<string, DiagnosticDescriptor> AttributeToDiagnostic = new()
    {
        // Actions (PRAG0400)
        ["MutationAttribute"] = NotPartialDiagnosticDescriptors.Prag0400,
        ["DomainActionAttribute"] = NotPartialDiagnosticDescriptors.Prag0400,

        // Persistence queries (PRAG0712) — [Query<TEntity>] / [Query<TEntity, TResult>]
        ["QueryAttribute"] = NotPartialDiagnosticDescriptors.Prag0712,

        // Actions - Boundary (PRAG0406)
        ["BoundaryAttribute"] = NotPartialDiagnosticDescriptors.Prag0406,

        // Endpoints (PRAG0500)
        ["EndpointAttribute"] = NotPartialDiagnosticDescriptors.Prag0500,

        // Persistence (PRAG0600)
        ["EntityAttribute"] = NotPartialDiagnosticDescriptors.Prag0600,
        ["RepositoryAttribute"] = NotPartialDiagnosticDescriptors.Prag0600,

        // Persistence - Database (PRAG0602)
        ["PragmaticDbContextAttribute"] = NotPartialDiagnosticDescriptors.Prag0602,

        // Messaging (PRAG0801)
        ["MessageHandlerAttribute"] = NotPartialDiagnosticDescriptors.Prag0801,

        // Ownership (PRAG1100)
        ["HasOwnerAttribute"] = NotPartialDiagnosticDescriptors.Prag1100,

        // Jobs (PRAG2502)
        ["JobAttribute"] = NotPartialDiagnosticDescriptors.Prag2502,
        ["RecurringJobAttribute"] = NotPartialDiagnosticDescriptors.Prag2502,

        // Mapping (PRAG0300)
        ["MapFromAttribute"] = NotPartialDiagnosticDescriptors.Prag0300,
        ["MapToAttribute"] = NotPartialDiagnosticDescriptors.Prag0300,

        // Caching (PRAG1700)
        ["CacheableAttribute"] = NotPartialDiagnosticDescriptors.Prag1700,

        // Patch (PRAG2200)
        ["GeneratePatchAttribute"] = NotPartialDiagnosticDescriptors.Prag2200,

        // Configuration (PRAG2000)
        ["ConfigurationAttribute"] = NotPartialDiagnosticDescriptors.Prag2000,
    };

    /// <summary>
    ///     Base type of every Pragmatic validation attribute. PRAG0200 is not triggered by an attribute on
    ///     the type but by validation attributes on its <i>properties</i> — same rule the generator applies
    ///     (see ValidationFeature.IsTypeWithPotentialValidation + ValidatableTransform).
    /// </summary>
    private const string ValidationAttributeBase = "Pragmatic.Validation.Attributes.ValidationAttribute";

    /// <summary>
    ///     Namespace prefixes that qualify an attribute as a Pragmatic attribute.
    /// </summary>
    private static readonly string[] PragmaticNamespacePrefixes =
    {
        "Pragmatic.Actions",
        "Pragmatic.Endpoints",
        "Pragmatic.Persistence",
        "Pragmatic.Messaging",
        "Pragmatic.Jobs",
        "Pragmatic.Mapping",
        "Pragmatic.Validation",
        "Pragmatic.Caching",
        "Pragmatic.Patch",
        "Pragmatic.Configuration",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            NotPartialDiagnosticDescriptors.Prag0200,
            NotPartialDiagnosticDescriptors.Prag0300,
            NotPartialDiagnosticDescriptors.Prag0400,
            NotPartialDiagnosticDescriptors.Prag0406,
            NotPartialDiagnosticDescriptors.Prag0500,
            NotPartialDiagnosticDescriptors.Prag0600,
            NotPartialDiagnosticDescriptors.Prag0602,
            NotPartialDiagnosticDescriptors.Prag0712,
            NotPartialDiagnosticDescriptors.Prag0801,
            NotPartialDiagnosticDescriptors.Prag1100,
            NotPartialDiagnosticDescriptors.Prag1700,
            NotPartialDiagnosticDescriptors.Prag2000,
            NotPartialDiagnosticDescriptors.Prag2200,
            NotPartialDiagnosticDescriptors.Prag2502);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;

        // Only check class declarations (not structs, records, etc.)
        if (typeSymbol.TypeKind != TypeKind.Class)
            return;

        // One declaration without `partial` is the defect, whatever the others say. Asking whether ANY
        // declaration is partial let a generator's own `partial class` part — Messaging and Jobs emit
        // one for a type that is not — silence the report on the user's declaration.
        var location = NonPartialDeclaration(typeSymbol, context.CancellationToken);
        if (location is null)
            return;

        // Check attributes for Pragmatic markers. One report per descriptor: a type carrying two
        // capabilities is missing `partial` for both, and each names what it cannot generate.
        var reported = new HashSet<string>();
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass is null) continue;

            // Handle generic attributes (e.g. EntityAttribute`1 → "EntityAttribute")
            var attrName = attrClass.IsGenericType
                ? attrClass.OriginalDefinition.Name
                : attrClass.Name;

            if (!AttributeToDiagnostic.TryGetValue(attrName, out var descriptor))
                continue;

            // Verify it's in a Pragmatic namespace
            var ns = GetFullNamespace(attrClass);
            if (!IsPragmaticNamespace(ns))
                continue;

            if (!reported.Add(descriptor.Id))
                continue;

            // Build message args — some descriptors use {0} only, others use {0} and {1}
            var shortAttrName = attrName.EndsWith("Attribute")
                ? attrName.Substring(0, attrName.Length - "Attribute".Length)
                : attrName;

            context.ReportDiagnostic(Diagnostic.Create(
                descriptor, location, typeSymbol.Name, shortAttrName));
        }

        // Validation (PRAG0200) has no marker attribute on the type: it is the properties that carry
        // [Required], [Range], ... — so it is checked last, and only if no type-level attribute matched.
        if (reported.Count == 0 && HasValidationAttributeOnProperty(typeSymbol))
            context.ReportDiagnostic(Diagnostic.Create(
                NotPartialDiagnosticDescriptors.Prag0200, location, typeSymbol.Name));
    }

    /// <summary>
    ///     The identifier of the first declaration of the type that lacks <c>partial</c>, or <c>null</c>
    ///     when every declaration has it.
    /// </summary>
    private static Location? NonPartialDeclaration(INamedTypeSymbol typeSymbol, System.Threading.CancellationToken ct)
    {
        foreach (var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(ct) is TypeDeclarationSyntax typeDecl
                && !typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
                return typeDecl.Identifier.GetLocation();
        }

        return null;
    }

    private static bool HasValidationAttributeOnProperty(INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            foreach (var attribute in property.GetAttributes())
                if (IsPragmaticValidationAttribute(attribute.AttributeClass))
                    return true;
        }

        return false;
    }

    private static bool IsPragmaticValidationAttribute(INamedTypeSymbol? attributeClass)
    {
        // Match on the base type, not the namespace: [AsyncValidate<T>] lives in the same namespace but
        // is not a validation rule, and the generator does not treat it as one either.
        for (var type = attributeClass; type is not null; type = type.BaseType)
            if (type.ToDisplayString() == ValidationAttributeBase)
                return true;

        return false;
    }

    private static string GetFullNamespace(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace) return string.Empty;
        return ns.ToDisplayString();
    }

    private static bool IsPragmaticNamespace(string ns)
    {
        foreach (var prefix in PragmaticNamespacePrefixes)
        {
            if (ns.StartsWith(prefix))
                return true;
        }
        return false;
    }
}
