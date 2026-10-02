using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Reports a validation attribute the Validation generator will silently drop (<c>PRAG0210</c>).
/// </summary>
/// <remarks>
///     <para>
///         The generator keeps an attribute only when its full name is one of
///         <c>Pragmatic.Validation.Attributes.*</c> or its type derives from that namespace's
///         <c>ValidationAttribute</c>. Everything else is skipped — but the emitted validator still
///         carries a <c>// Validate {Property}</c> comment for the property, so the output reads as
///         though a check was produced. A consumer shipped <c>[Range(0.01, …)]</c> and
///         <c>[EmailAddress]</c> from <c>System.ComponentModel.DataAnnotations</c> on a mutation and
///         got a validator with an empty block; negative amounts and malformed addresses reached the
///         database.
///     </para>
///     <para>
///         Deliberately not a code fix that rewrites the attribute: the semantics are close but not
///         identical (<c>StringLength</c> against <c>Length</c>, <c>ErrorMessage</c>, culture-sensitive
///         <c>Range</c> on strings), and translating them silently would replace a visible gap with an
///         invisible difference. The message names the equivalent and the developer chooses.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoredValidationAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string PragmaticAttributeNamespace = "Pragmatic.Validation.Attributes";
    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";

    /// <summary>
    ///     The DataAnnotations attributes that have a Pragmatic counterpart, so the message can name
    ///     it. An attribute missing from this map is still reported, just without a suggestion.
    /// </summary>
    private static readonly Dictionary<string, string> Equivalents = new()
    {
        ["RequiredAttribute"] = "Required",
        ["RangeAttribute"] = "Range",
        ["MinLengthAttribute"] = "MinLength",
        ["MaxLengthAttribute"] = "MaxLength",
        ["StringLengthAttribute"] = "Length",
        ["EmailAddressAttribute"] = "Email",
        ["PhoneAttribute"] = "Phone",
        ["UrlAttribute"] = "Url",
        ["CreditCardAttribute"] = "CreditCard",
        ["RegularExpressionAttribute"] = "Regex",
    };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(IgnoredValidationAttributeDescriptors.Prag0210);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // No Pragmatic.Validation in the compilation means no generator to be silent about.
        var validationBase = context.Compilation
            .GetTypeByMetadataName(PragmaticAttributeNamespace + ".ValidationAttribute");
        if (validationBase is null)
            return;

        // The DataAnnotations base, when the compilation has it. What derives from it reads as
        // validation and is dropped by the Pragmatic generator — wherever it happens to be declared.
        var dataAnnotationsBase = context.Compilation
            .GetTypeByMetadataName(DataAnnotationsNamespace + ".ValidationAttribute");

        context.RegisterSymbolAction(
            c => Analyze(c, validationBase, dataAnnotationsBase), SymbolKind.Property);
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        INamedTypeSymbol validationBase,
        INamedTypeSymbol? dataAnnotationsBase)
    {
        var property = (IPropertySymbol)context.Symbol;

        foreach (var attribute in property.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
                continue;

            // ⚠️ The question is not where the attribute lives, it is whether the Pragmatic generator
            // will emit a check for it — and the answer is no for anything deriving from the
            // DataAnnotations base rather than this framework's. Asking about the namespace instead let
            // three of Pragmatic's own attributes through: the money rules derived from the
            // DataAnnotations base and sat in Pragmatic.Internationalization.AspNetCore.Validation, so
            // the one guard written for this mistake could not see them.
            var looksLikeValidation =
                attributeClass.ContainingNamespace?.ToDisplayString() == DataAnnotationsNamespace
                || (dataAnnotationsBase is not null && DerivesFrom(attributeClass, dataAnnotationsBase));

            if (!looksLikeValidation)
                continue;

            if (DerivesFrom(attributeClass, validationBase))
                continue;

            var suggestion = Equivalents.TryGetValue(attributeClass.Name, out var pragmatic)
                ? $" — use [{pragmatic}] from {PragmaticAttributeNamespace}"
                : string.Empty;

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken)
                ?.GetLocation() ?? property.Locations[0];

            context.ReportDiagnostic(Diagnostic.Create(
                IgnoredValidationAttributeDescriptors.Prag0210,
                location,
                attributeClass.Name,
                suggestion));
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;

        return false;
    }
}
