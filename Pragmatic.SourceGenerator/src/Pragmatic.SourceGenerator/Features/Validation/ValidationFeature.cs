using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Validation.Diagnostics;
using Pragmatic.SourceGenerator.Features.Validation.Models;
using Pragmatic.SourceGenerator.Features.Validation.Templates;
using Pragmatic.SourceGenerator.Features.Validation.Transforms;

namespace Pragmatic.SourceGenerator.Features.Validation;

/// <summary>
///     Validation feature module for the unified Pragmatic source generator.
///     Generates:
///     <list type="bullet">
///         <item>ISyncValidator.Validate() for types with validation attributes</item>
///         <item>DI registration for [Validator] classes</item>
///         <item>IAsyncValidatorBindings&lt;T&gt; for [AsyncValidate&lt;T&gt;] bindings</item>
///         <item>[assembly: PragmaticMetadata(...)] when Composition is referenced</item>
///     </list>
/// </summary>
internal static partial class ValidationFeature
{
    private const string ValidatorAttributeFullName = "Pragmatic.Validation.Attributes.ValidatorAttribute";
    private const string AsyncValidateAttributeFullName = "Pragmatic.Validation.Attributes.AsyncValidateAttribute`1";

    /// <returns>
    ///     The validator registration this compilation generates, for a host that declares its own
    ///     <c>[Validator]</c> classes.
    /// </returns>
    public static IncrementalValueProvider<EquatableArray<Composition.Models.MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DetectedFeatures> features)
    {
        // Part 1: Generate ISyncValidator for types with validation attributes
        var validatableProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                IsTypeWithPotentialValidation,
                TransformToValidatableModel)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.ValidationValidatables);

        // A rule may name its message with a TKeys constant, which the I18n feature writes: while the
        // rule is read the constant does not exist, and its catalog is what says the key.
        context.RegisterSourceOutputSafe(
            validatableProvider.Combine(I18n.I18nFeature.KeyConstantCatalog(context)).Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasValidation)
                    return;
                var (model, keyConstants) = pair.Left;
                GenerateValidatable(ctx, ResolveMessageKeys(ctx, model, keyConstants));
            });

        // Part 2: Process [Validator] classes for DI registration
        var validatorProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ValidatorAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax,
                TransformToValidatorModel)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .WithTrackingName(TrackingNames.ValidationValidators);

        // Part 3: Process [AsyncValidate<T>] bindings on properties and classes
        // Note: transform returns array because a single symbol may have multiple [AsyncValidate<T>] attributes
        var asyncBindingProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AsyncValidateAttributeFullName,
                static (node, _) => node is ClassDeclarationSyntax or PropertyDeclarationSyntax,
                TransformToAsyncBindings)
            .Where(static m => !m.IsDefaultOrEmpty)
            .SelectMany(static (m, _) => m)
            .WithTrackingName(TrackingNames.ValidationAsyncBindings);

        // Group bindings by entity type to generate one IAsyncValidatorBindings<T> per entity
        var allAsyncBindings = asyncBindingProvider.Collect();
        context.RegisterSourceOutputSafe(
            allAsyncBindings.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasValidation)
                    return;
                GenerateAsyncValidatorBindings(ctx, pair.Left);
            });

        // Part 4: Generate ISyncValidator for endpoint body DTOs
        // Predicts which body DTOs the Endpoints SG will generate and adds validation
        var bodyDtoProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Pragmatic.Endpoints.Attributes.EndpointAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                BodyDtoValidationTransform.Transform)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutputSafe(
            bodyDtoProvider.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasValidation)
                    return;
                GenerateValidatable(ctx, pair.Left);
            });

        // Combine validators + async bindings for unified DI registration
        var allValidators = validatorProvider.Collect();
        var registrationData = allValidators.Combine(allAsyncBindings);
        context.RegisterSourceOutputSafe(
            registrationData.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasValidation)
                    return;
                GenerateRegistration(ctx, pair.Left);
            });

        // Phase 1.1: emit [assembly: PragmaticMetadata(MetadataCategory.Validation, ...)]
        // Only when Pragmatic.Composition is referenced (enables cross-assembly discovery)
        var compilationInfo = context.CompilationProvider.Select(static (c, _) => (
            IsDebug: c.Options.OptimizationLevel == OptimizationLevel.Debug,
            HasComposition: CompositionDetector.IsCompositionReferenced(c)));
        var metadataInput = registrationData.Combine(compilationInfo);
        context.RegisterSourceOutputSafe(
            metadataInput.Combine(features),
            static (ctx, pair) =>
            {
                if (!pair.Right.HasValidation)
                    return;
                GenerateValidationMetadata(ctx, pair.Left);
            });

        return metadataInput.Combine(features).Select(static (pair, _) =>
            pair.Right.HasValidation
                ? LocalValidationRegistrations(pair.Left)
                : EquatableArray<Composition.Models.MetadataEntry>.Empty);
    }

    private static bool IsTypeWithPotentialValidation(SyntaxNode node, CancellationToken _)
    {
        if (node is not TypeDeclarationSyntax typeDecl)
            return false;

        var isPartial = typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

        foreach (var member in typeDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax prop)
                continue;

            // Properties with 'required' modifier (implicit [Required]) — only for partial types
            if (isPartial && prop.Modifiers.Any(SyntaxKind.RequiredKeyword))
                return true;

            // Any attribute at all, and the semantic stage decides whether it is a rule.
            //
            // ⚠️ Not the attribute's simple name against a hardcoded list: that would make a whole
            // documented capability unreachable. A type whose only rule is one the consumer wrote would
            // never be offered to the semantic stage, so it would produce no validator and no
            // diagnostic — the rule would not exist. It would appear to work as soon as a known rule
            // sat beside it, and the first thing anyone writes beside a custom rule is [Required].
            //
            // Accepting any attribute here is safe because the real filter is downstream:
            // ExtractValidationAttributes keeps an attribute only when it derives from
            // Pragmatic.Validation.Attributes.ValidationAttribute, and the transform returns null when
            // a type has none — so an attributed property that carries nothing of ours still generates
            // nothing. What this pass owes is candidates, not answers.
            foreach (var attrList in prop.AttributeLists)
                if (attrList.Attributes.Count > 0)
                    return true;
        }

        return false;
    }

    #region Helpers

    /// <summary>
    ///     Checks if the type has [Entity] or [Entity] attribute.
    ///     Entity types get change-aware Validate(IReadOnlySet&lt;string&gt;?) generated.
    /// </summary>
    private static bool HasEntityAttribute(INamedTypeSymbol typeSymbol)
    {
        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            if (attrClass.ToDisplayString() == "Pragmatic.Persistence.Entity.EntityAttribute")
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Builds cross-property dependency map for change-tracking-aware validation.
    ///     When property X has [GreaterThanProperty(nameof(Y))], modifying Y should re-validate X.
    /// </summary>
    private static EquatableArray<PropertyDependencyModel> BuildPropertyDependencies(
        ImmutableArray<PropertyValidationModel> properties)
    {
        var deps = new Dictionary<string, List<string>>();

        foreach (var prop in properties)
            foreach (var attr in prop.Attributes)
            {
                if (attr.OtherProperty is null)
                    continue;

                if (!deps.TryGetValue(attr.OtherProperty, out var list))
                {
                    list = new List<string>();
                    deps[attr.OtherProperty] = list;
                }

                if (!list.Contains(prop.PropertyName))
                    list.Add(prop.PropertyName);
            }

        // Deterministic order (ordinal by key) so equal inputs always produce an equal model.
        return deps
            .OrderBy(kvp => kvp.Key, System.StringComparer.Ordinal)
            .Select(kvp => new PropertyDependencyModel
            {
                Property = kvp.Key,
                Dependents = kvp.Value.ToImmutableArray()
            })
            .ToImmutableArray();
    }

    #endregion
}
