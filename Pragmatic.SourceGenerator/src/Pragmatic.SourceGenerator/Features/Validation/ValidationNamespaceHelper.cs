using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation;

/// <summary>
///     Derives namespace prefix from validator and async binding models.
///     Shared between ValidatorRegistrationTemplate and ValidationMetadataTemplate.
/// </summary>
internal static class ValidationNamespaceHelper
{
    internal static string DeriveNamespacePrefix(
        ImmutableArray<ValidatorModel> validators,
        ImmutableArray<AsyncValidatorBindingsModel> asyncBindings)
    {
        var namespaces = new List<string>();

        if (!validators.IsDefaultOrEmpty)
            namespaces.AddRange(validators
                .Select(v => v.Namespace)
                .Where(ns => !string.IsNullOrEmpty(ns)));

        if (!asyncBindings.IsDefaultOrEmpty)
            namespaces.AddRange(asyncBindings
                .Select(b => b.Namespace)
                .Where(ns => !string.IsNullOrEmpty(ns)));

        if (namespaces.Count == 0)
            return string.Empty;

        return NamespacePrefixHelper.DerivePrefix(namespaces);
    }
}
