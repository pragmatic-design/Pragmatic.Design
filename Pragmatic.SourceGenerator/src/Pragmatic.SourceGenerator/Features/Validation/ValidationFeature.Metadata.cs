using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Validation.Models;
using Pragmatic.SourceGenerator.Features.Validation.Templates;

namespace Pragmatic.SourceGenerator.Features.Validation;

/// <summary>
///     Metadata generation methods for Validation feature.
/// </summary>
internal static partial class ValidationFeature
{
    #region Metadata

    private static void GenerateValidationMetadata(
        SourceProductionContext context,
        ((ImmutableArray<ValidatorModel> Validators, ImmutableArray<AsyncBindingEntry> AsyncBindings) Registration,
            (bool IsDebug, bool HasComposition) CompilationInfo) input)
    {
        var ((validators, asyncBindingEntries), (isDebug, hasComposition)) = input;

        if (!hasComposition)
            return;

        var asyncBindings = GroupAsyncBindings(asyncBindingEntries);

        var validValidators = validators.IsDefaultOrEmpty
            ? ImmutableArray<ValidatorModel>.Empty
            : validators.Where(v => !v.MissingValidatorInterface).ToImmutableArray();

        if (validValidators.IsDefaultOrEmpty && asyncBindings.IsDefaultOrEmpty)
            return;

        var template = new ValidationMetadataTemplate(validValidators, asyncBindings, isDebug);
        var artifact = template.RenderOutput();
        context.AddSource(artifact);
    }

    /// <summary>
    ///     The validator registration this compilation generates, for a host that declares its own
    ///     <c>[Validator]</c> classes: the metadata attribute above is emitted into this same
    ///     compilation, so no host can read it back off this assembly.
    /// </summary>
    /// <remarks>
    ///     Mirrors <see cref="GenerateValidationMetadata" /> condition for condition — the host must be
    ///     told to call the registration exactly when one is generated.
    /// </remarks>
    private static EquatableArray<MetadataEntry> LocalValidationRegistrations(
        ((ImmutableArray<ValidatorModel> Validators, ImmutableArray<AsyncBindingEntry> AsyncBindings) Registration,
            (bool IsDebug, bool HasComposition) CompilationInfo) input)
    {
        var ((validators, asyncBindingEntries), (_, hasComposition)) = input;

        if (!hasComposition)
            return EquatableArray<MetadataEntry>.Empty;

        var asyncBindings = GroupAsyncBindings(asyncBindingEntries);

        var validValidators = validators.IsDefaultOrEmpty
            ? ImmutableArray<ValidatorModel>.Empty
            : validators.Where(v => !v.MissingValidatorInterface).ToImmutableArray();

        if (validValidators.IsDefaultOrEmpty && asyncBindings.IsDefaultOrEmpty)
            return EquatableArray<MetadataEntry>.Empty;

        return ImmutableArray.Create(
            HostLocalRegistration.Create(
                MetadataCategoryIds.Validation,
                MetadataSchemaVersions.Validation,
                ValidatorRegistrationTemplate.FqnFor(validValidators, asyncBindings)));
    }

    #endregion
}
