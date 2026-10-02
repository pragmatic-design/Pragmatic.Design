using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.I18n.Models;
using Pragmatic.SourceGenerator.Features.Validation.Diagnostics;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation;

/// <summary>
///     A rule's <c>MessageKey</c> that names a <c>TKeys</c> constant, read through the constants' catalog.
/// </summary>
/// <remarks>
///     The constant is this generator's own output, so the transform found no value in the argument and
///     kept it as written. The catalog the I18n feature builds from the same model says which key the
///     constant holds. A reference it does not hold is reported, and the rule keeps its default
///     key — said, now, rather than done in silence.
/// </remarks>
internal static partial class ValidationFeature
{
    private static ValidatableModel ResolveMessageKeys(
        SourceProductionContext context, ValidatableModel model, TranslationKeyConstantCatalog catalog)
    {
        if (!model.Properties.Any(p => p.Attributes.Any(a => a.MessageKeyReference is not null)))
            return model;

        return model with
        {
            Properties =
            [
                .. model.Properties.Select(property => property with
                {
                    Attributes = [.. property.Attributes.Select(attr => Resolve(context, model, property, attr, catalog))]
                })
            ]
        };
    }

    private static ValidationAttributeModel Resolve(
        SourceProductionContext context, ValidatableModel model, PropertyValidationModel property,
        ValidationAttributeModel attr, TranslationKeyConstantCatalog catalog)
    {
        if (attr.MessageKeyReference is not { } reference)
            return attr;

        if (catalog.KeyOf(reference) is { } key)
            return attr with { MessageKey = key, MessageKeyReference = null };

        context.ReportDiagnostic(Diagnostic.Create(
            ValidationDiagnostics.MessageKeyCannotBeRead,
            attr.MessageKeyLocation?.ToLocation(),
            model.TypeName,
            property.PropertyName,
            reference));
        return attr;
    }
}
