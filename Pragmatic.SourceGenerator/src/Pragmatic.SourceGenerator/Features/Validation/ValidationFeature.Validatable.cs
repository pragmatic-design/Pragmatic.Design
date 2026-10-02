using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Validation.Diagnostics;
using Pragmatic.SourceGenerator.Features.Validation.Models;
using Pragmatic.SourceGenerator.Features.Validation.Templates;
using Pragmatic.SourceGenerator.Features.Validation.Transforms;

namespace Pragmatic.SourceGenerator.Features.Validation;

/// <summary>
///     Validatable (ISyncValidator) generation methods.
/// </summary>
internal static partial class ValidationFeature
{
    private static ValidatableModel? TransformToValidatableModel(
        GeneratorSyntaxContext context,
        CancellationToken ct)
    {
        if (context.Node is not TypeDeclarationSyntax typeDecl)
            return null;

        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDecl, ct);
        if (symbol is not INamedTypeSymbol typeSymbol)
            return null;

        var isEntity = HasEntityAttribute(typeSymbol);

        var properties = ValidatableTransform.ExtractValidatedProperties(
            typeSymbol, context.SemanticModel.Compilation, ct, isEntity);

        if (properties.IsDefaultOrEmpty)
            return null;

        var isPartial = ValidatableTransform.IsPartialType(typeDecl);

        // Readable by name: what a rule can name, and what GetPropertyValue can answer for. An indexer
        // has no name to read by, a property without a getter no value to read.
        var allProps = typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic
                        && !p.IsIndexer && p.GetMethod is not null)
            .ToList();

        // Build cross-property dependency map for entities
        var propertyDependencies = isEntity
            ? BuildPropertyDependencies(properties)
            : EquatableArray<Models.PropertyDependencyModel>.Empty;

        return new ValidatableModel
        {
            Namespace = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : typeSymbol.ContainingNamespace.ToDisplayString(),
            TypeName = typeSymbol.Name,
            Accessibility = typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            TypeKind = ValidatableTransform.GetTypeKind(typeSymbol),
            IsRecord = typeSymbol.IsRecord,
            IsValueType = typeSymbol.IsValueType,
            IsPartial = isPartial,
            ContainingTypes = ContainingTypesOf(typeSymbol, ct),
            IsEntity = isEntity,
            Properties = properties,
            HasGroups = properties.Any(p => p.Attributes.Any(a => !a.Groups.IsDefaultOrEmpty)),
            AllPropertyNames = allProps.Select(p => p.Name).ToImmutableArray(),
            AllPropertyTypes = allProps
                .Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToImmutableArray(),
            PropertyDependencies = propertyDependencies
        };
    }

    /// <summary>
    ///     Whether this feature will emit a <c>Validate()</c> for this type — the same gates the
    ///     pipeline puts in front of <see cref="GenerateValidatable"/>, asked before generation has run.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the one place the criterion lives. <c>ValidatablePredictor</c> asks it for a
    ///         property's type, so that a generated call to <c>Validate()</c> exists exactly when the
    ///         method will: a second criterion written elsewhere drifted from this one, and a
    ///         <c>required</c>-only child had a <c>Validate()</c> nobody called.
    ///     </para>
    ///     <para>
    ///         ⚠️ Three gates, not one, and the first is syntactic. A type enters the pipeline only
    ///         through <see cref="IsTypeWithPotentialValidation"/>, which looks at the declared
    ///         attributes and <c>required</c> members; a type whose only validatable content is a
    ///         collection of validatables passes the property selection and still gets nothing,
    ///         because it never entered. Answering from the selection alone emitted a call to a
    ///         <c>Validate()</c> that was never written — CS0103 in a generated file.
    ///     </para>
    /// </remarks>
    internal static bool WillGenerateValidatorFor(INamedTypeSymbol typeSymbol, Compilation compilation, CancellationToken ct)
    {
        var entersThePipeline = false;
        var isPartial = false;
        foreach (var reference in typeSymbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(ct) is not TypeDeclarationSyntax declaration)
                continue;

            entersThePipeline |= IsTypeWithPotentialValidation(declaration, ct);
            isPartial |= ValidatableTransform.IsPartialType(declaration);
        }

        // Not partial: PRAG0200 is reported and nothing is generated (GenerateValidatable returns).
        if (!entersThePipeline || !isPartial)
            return false;

        return !ValidatableTransform.ExtractValidatedProperties(
            typeSymbol, compilation, ct, HasEntityAttribute(typeSymbol)).IsDefaultOrEmpty;
    }

    /// <summary>
    ///     The types enclosing <paramref name="typeSymbol" />, outermost first.
    /// </summary>
    private static EquatableArray<Models.ContainingTypeModel> ContainingTypesOf(
        INamedTypeSymbol typeSymbol,
        CancellationToken ct)
    {
        if (typeSymbol.ContainingType is null)
            return EquatableArray<Models.ContainingTypeModel>.Empty;

        var chain = new List<Models.ContainingTypeModel>();
        for (var container = typeSymbol.ContainingType; container is not null; container = container.ContainingType)
        {
            ct.ThrowIfCancellationRequested();

            // Every declaration counts: `partial` on any one of them makes the type reopenable.
            var isPartial = container.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax(ct))
                .OfType<TypeDeclarationSyntax>()
                .Any(ValidatableTransform.IsPartialType);

            chain.Add(new Models.ContainingTypeModel
            {
                TypeName = container.Name,
                TypeKind = ValidatableTransform.GetTypeKind(container),
                Accessibility = container.DeclaredAccessibility.ToString().ToLowerInvariant(),
                IsPartial = isPartial,
                IsStatic = container.IsStatic
            });
        }

        chain.Reverse();
        return chain.ToImmutableArray();
    }

    private static void GenerateValidatable(SourceProductionContext context, ValidatableModel model)
    {
        // Not partial: nothing to generate into. PRAG0200 is the companion analyzer's.
        if (!model.IsPartial)
            return;

        // The generated file declares each container again, which C# allows only for a partial type.
        // Emitting anyway would produce an error inside a file the author cannot open, about a modifier
        // they did not write — so name the type to change instead.
        foreach (var container in model.ContainingTypes)
        {
            if (container.IsPartial)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                ValidationDiagnostics.ContainingTypeMustBePartial,
                Location.None,
                model.TypeName,
                container.TypeName));
            return;
        }

        ValidateCrossPropertyReferences(context, model);
        ValidateEnumAttributes(context, model);
        ValidateElementValidation(context, model);

        var template = new ValidatableTemplate(model);
        var artifact = template.RenderOutput();

        context.AddSource(artifact);
    }

    private static void ValidateCrossPropertyReferences(SourceProductionContext context, ValidatableModel model)
    {
        var allPropertyNames = new HashSet<string>(model.AllPropertyNames);

        var propertyTypeByName = new Dictionary<string, string>();
        for (var i = 0; i < model.AllPropertyNames.Length; i++)
            propertyTypeByName[model.AllPropertyNames[i]] = model.AllPropertyTypes[i];

        foreach (var prop in model.Properties)
            foreach (var attr in prop.Attributes)
            {
                if (string.IsNullOrEmpty(attr.OtherProperty))
                    continue;

                var otherProp = attr.OtherProperty!;

                // PRAG0203: Referenced property doesn't exist
                if (!allPropertyNames.Contains(otherProp))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ValidationDiagnostics.ComparisonPropertyNotFound,
                        Location.None,
                        otherProp, attr.AttributeName, prop.PropertyName));
                    continue;
                }

                // PRAG0209: Incompatible types for comparison
                if (attr.Kind is ValidationKind.EqualTo or ValidationKind.NotEqualTo
                    or ValidationKind.GreaterThanProperty or ValidationKind.LessThanProperty
                    or ValidationKind.GreaterThanOrEqualProperty or ValidationKind.LessThanOrEqualProperty)
                    if (propertyTypeByName.TryGetValue(otherProp, out var otherType)
                        && prop.PropertyType != otherType)
                        context.ReportDiagnostic(Diagnostic.Create(
                            ValidationDiagnostics.IncompatibleComparisonTypes,
                            Location.None,
                            prop.PropertyName, prop.PropertyType,
                            otherProp, otherType));
            }
    }

    /// <summary>
    ///     PRAG0220: <c>[ValidEnum]</c> on a property whose type is not an enum.
    /// </summary>
    private static void ValidateEnumAttributes(SourceProductionContext context, ValidatableModel model)
    {
        foreach (var prop in model.Properties)
        {
            if (prop.IsEnum)
                continue;

            foreach (var attr in prop.Attributes)
                if (attr.Kind == ValidationKind.ValidEnum)
                    context.ReportDiagnostic(Diagnostic.Create(
                        ValidationDiagnostics.ValidEnumOnNonEnum,
                        Location.None,
                        prop.PropertyName, prop.PropertyType));
        }
    }

    private static void ValidateElementValidation(SourceProductionContext context, ValidatableModel model)
    {
        foreach (var prop in model.Properties)
        {
            // ⚠️ DeclaresValidateElements, not ValidatesElements: the second is true of every
            // collection whose element type is validatable, attribute or not. Asking the wrong one
            // made PRAG0223 accuse six properties that carry no attribute at all — and would have made
            // PRAG0204 and PRAG0205 accuse the same, had their own conditions not happened to exclude
            // it.
            if (!prop.DeclaresValidateElements)
                continue;

            // PRAG0204: [ValidateElements] on non-collection type
            if (!prop.IsCollection)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ValidationDiagnostics.ValidateElementsOnNonCollection,
                    Location.None,
                    prop.PropertyName));
                continue;
            }

            // PRAG0205: Collection element type doesn't implement ISyncValidator
            if (!prop.ElementIsValidatable)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ValidationDiagnostics.ValidateElementsNotValidatable,
                    Location.None,
                    prop.ElementType ?? "unknown", prop.PropertyName));
                continue;
            }

            // PRAG0223: legal, and configuring nothing — but only where the type would be validated
            // anyway. The walk comes from the element type being an ISyncValidator, which is the very
            // condition PRAG0205 above enforces, so the bare attribute adds nothing to it.
            //
            // ⚠️ Except when it is the type's ONLY reason to have a validator at all. Measured while
            // building this: a record whose sole annotation is [ValidateElements] on a list generates
            // no validator without it, and four cases of the suite went red with "generatedSource is
            // null" rather than on any assertion about content. There the attribute decides something
            // real — that the validator exists — and refusing it would remove a capability with no
            // replacement. The issue's premise held for the common case and not for that one.
            if (!prop.ValidateElementsStopOnFirst && ValidatedWithoutTheAttribute(model))
                context.ReportDiagnostic(Diagnostic.Create(
                    ValidationDiagnostics.ValidateElementsConfiguresNothing,
                    Location.None,
                    prop.PropertyName, prop.ElementType ?? "its element type"));
        }
    }

    /// <summary>
    ///     Whether the type would still get a validator with a bare <c>[ValidateElements]</c>
    ///     deleted — the condition that makes writing it redundant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This mirrors <c>IsTypeWithPotentialValidation</c>, the syntactic gate a type passes
    ///         to enter the pipeline at all: a <c>required</c> member, or any attribute on any
    ///         property. Read from the model, that is a rule extracted on some property or a
    ///         <c>required</c> modifier.
    ///     </para>
    ///     <para>
    ///         ⚠️ Another bare <c>[ValidateElements]</c> on the type does <b>not</b> count, although it
    ///         does keep the type in the pipeline. Two of them are each redundant on their own and not
    ///         together: accusing both would be advice that, followed, leaves the type with no
    ///         validator at all. The same reasoning makes the other direction safe — a property
    ///         attributed with something outside <c>Pragmatic.Validation</c> passes the syntactic gate
    ///         and leaves no rule in the model, so PRAG0223 stays silent where it might have fired. A
    ///         missed one costs a redundant attribute; a wrong one costs a working declaration.
    ///     </para>
    /// </remarks>
    private static bool ValidatedWithoutTheAttribute(ValidatableModel model)
        => model.Properties.Any(other => other.Attributes.Length > 0 || other.IsRequiredModifier);
}
