using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Validation.Diagnostics;
using Pragmatic.SourceGenerator.Features.Validation.Models;
using Pragmatic.SourceGenerator.Features.Validation.Templates;
using Pragmatic.SourceGenerator.Features.Validation.Transforms;

namespace Pragmatic.SourceGenerator.Features.Validation;

/// <summary>
///     Validator and AsyncValidator generation methods.
/// </summary>
internal static partial class ValidationFeature
{
    #region AsyncValidate bindings

    /// <summary>
    ///     Intermediate record for a single [AsyncValidate&lt;T&gt;] occurrence.
    ///     Contains entity info + single binding to be grouped later.
    /// </summary>
    private sealed record AsyncBindingEntry(
        string EntityNamespace,
        string EntityTypeName,
        string EntityFullTypeName,
        AsyncValidatorBindingModel Binding);

    private static ImmutableArray<AsyncBindingEntry> TransformToAsyncBindings(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        string? triggerPropertyName = null;
        INamedTypeSymbol entitySymbol;

        if (context.TargetSymbol is IPropertySymbol propertySymbol)
        {
            triggerPropertyName = propertySymbol.Name;
            entitySymbol = propertySymbol.ContainingType;
        }
        else if (context.TargetSymbol is INamedTypeSymbol typeSymbol)
        {
            entitySymbol = typeSymbol;
        }
        else
        {
            return ImmutableArray<AsyncBindingEntry>.Empty;
        }

        var entityNs = entitySymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : entitySymbol.ContainingNamespace.ToDisplayString();
        var entityName = entitySymbol.Name;
        var entityFullName = entitySymbol.ToDisplayString();

        var entries = ImmutableArray.CreateBuilder<AsyncBindingEntry>();

        foreach (var attrData in context.Attributes)
        {
            var attrClass = attrData.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType)
                continue;

            if (attrClass.TypeArguments.Length != 1)
                continue;

            var validatorType = attrClass.TypeArguments[0];
            var validatorFullName = validatorType.ToDisplayString();

            entries.Add(new AsyncBindingEntry(
                entityNs,
                entityName,
                entityFullName,
                new AsyncValidatorBindingModel
                {
                    ValidatorFullTypeName = validatorFullName,
                    TriggerPropertyName = triggerPropertyName
                }));
        }

        return entries.ToImmutable();
    }

    private static void GenerateAsyncValidatorBindings(
        SourceProductionContext context,
        ImmutableArray<AsyncBindingEntry> entries)
    {
        foreach (var model in GroupAsyncBindings(entries))
        {
            var template = new AsyncValidatorBindingsTemplate(model);
            var artifact = template.RenderOutput();
            context.AddSource(artifact);
        }
    }

    /// <summary>Groups async binding entries by entity type.</summary>
    private static ImmutableArray<AsyncValidatorBindingsModel> GroupAsyncBindings(
        ImmutableArray<AsyncBindingEntry> entries)
    {
        if (entries.IsDefaultOrEmpty)
            return ImmutableArray<AsyncValidatorBindingsModel>.Empty;

        return entries
            .GroupBy(e => e.EntityFullTypeName)
            .Select(g =>
            {
                var first = g.First();
                return new AsyncValidatorBindingsModel
                {
                    Namespace = first.EntityNamespace,
                    TypeName = first.EntityTypeName,
                    FullTypeName = first.EntityFullTypeName,
                    Bindings = g.Select(e => e.Binding).ToImmutableArray()
                };
            })
            .ToImmutableArray();
    }

    #endregion

    #region Validator (DI registration)

    private static ValidatorModel? TransformToValidatorModel(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var validatedType = FindValidatedType(typeSymbol);

        // PRAG0201: [Validator] must implement IAsyncValidator<T>
        if (validatedType is null)
        {
            return new ValidatorModel
            {
                Namespace = typeSymbol.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : typeSymbol.ContainingNamespace.ToDisplayString(),
                ValidatorTypeName = typeSymbol.Name,
                ValidatorFullName = typeSymbol.ToDisplayString(),
                Accessibility = GetAccessibility(typeSymbol),
                ValidatedType = new ValidatedTypeModel
                {
                    Namespace = "",
                    TypeName = "Unknown",
                    FullName = "Unknown",
                    IsValueType = false
                },
                Lifetime = ServiceLifetimeKind.Scoped,
                ValidatedTypeHasSyncValidation = false,
                LocationInfo = LocationInfo.From(context.TargetNode.GetLocation()),
                MissingValidatorInterface = true
            };
        }

        var lifetime = GetLifetimeFromAttribute(context.Attributes);
        var hasSyncValidation = ValidatableTransform.HasValidationAttributes(validatedType);

        return new ValidatorModel
        {
            Namespace = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : typeSymbol.ContainingNamespace.ToDisplayString(),
            ValidatorTypeName = typeSymbol.Name,
            ValidatorFullName = typeSymbol.ToDisplayString(),
            Accessibility = GetAccessibility(typeSymbol),
            ValidatedType = new ValidatedTypeModel
            {
                Namespace = validatedType.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : validatedType.ContainingNamespace.ToDisplayString(),
                TypeName = validatedType.Name,
                FullName = validatedType.ToDisplayString(),
                IsValueType = validatedType.IsValueType
            },
            Lifetime = lifetime,
            ValidatedTypeHasSyncValidation = hasSyncValidation,
            LocationInfo = LocationInfo.From(context.TargetNode.GetLocation()),
            OperationDeclaredIn = OperationDeclaredElsewhere(validatedType, context.SemanticModel.Compilation)
        };
    }

    /// <summary>
    ///     The assembly name when <paramref name="validatedType" /> is a mutation or a domain action,
    ///     declared outside <paramref name="compilation" />, whose async validation is not switched on by
    ///     <c>[Validate]</c> — the one shape in which this validator is registered and never called.
    /// </summary>
    private static string? OperationDeclaredElsewhere(INamedTypeSymbol validatedType, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(validatedType.ContainingAssembly, compilation.Assembly))
            return null;

        var attributes = validatedType.GetAttributes()
            .Select(a => a.AttributeClass?.ToDisplayString())
            .ToList();

        var isOperation = attributes.Contains(AttributeNames.DomainAction) || attributes.Contains(AttributeNames.Mutation);
        var validateIsDeclared = attributes.Contains("Pragmatic.Actions.Attributes.ValidateAttribute");

        return isOperation && !validateIsDeclared ? validatedType.ContainingAssembly?.Name : null;
    }

    private static INamedTypeSymbol? FindValidatedType(INamedTypeSymbol typeSymbol)
    {
        const string asyncValidatorInterface = "Pragmatic.Validation.IAsyncValidator<T>";

        foreach (var iface in typeSymbol.AllInterfaces)
            if (iface.OriginalDefinition.ToDisplayString() == asyncValidatorInterface)
                if (iface.TypeArguments.Length == 1 && iface.TypeArguments[0] is INamedTypeSymbol validatedType)
                    return validatedType;

        return null;
    }

    private static ServiceLifetimeKind GetLifetimeFromAttribute(
        ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
            if (attr.AttributeClass?.ToDisplayString() == ValidatorAttributeFullName)
                foreach (var namedArg in attr.NamedArguments)
                    if (namedArg is { Key: "Lifetime", Value.Value: int lifetimeValue })
                        return (ServiceLifetimeKind)lifetimeValue;

        return ServiceLifetimeKind.Scoped;
    }

    private static string GetAccessibility(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            _ => "internal"
        };
    }

    private static void GenerateRegistration(
        SourceProductionContext context,
        (ImmutableArray<ValidatorModel> Validators, ImmutableArray<AsyncBindingEntry> AsyncBindings) data)
    {
        var (validators, asyncBindingEntries) = data;
        var asyncBindings = GroupAsyncBindings(asyncBindingEntries);

        if (validators.IsDefaultOrEmpty && asyncBindings.IsDefaultOrEmpty)
            return;

        var validValidators = ImmutableArray<ValidatorModel>.Empty;
        if (!validators.IsDefaultOrEmpty)
        {
            var validList = new List<ValidatorModel>();
            foreach (var validator in validators)
            {
                if (validator.MissingValidatorInterface)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ValidationDiagnostics.ValidatorMustImplementInterface,
                        validator.Location ?? Location.None,
                        validator.ValidatorTypeName));
                    continue;
                }

                if (validator.OperationDeclaredIn is { } operationAssembly)
                    context.ReportDiagnostic(Diagnostic.Create(
                        ValidationDiagnostics.AsyncValidatorOutsideTheOperationsAssembly,
                        validator.Location ?? Location.None,
                        validator.ValidatorTypeName,
                        validator.ValidatedType.TypeName,
                        operationAssembly));

                validList.Add(validator);
            }

            validValidators = validList.ToImmutableArray();
        }

        if (validValidators.IsDefaultOrEmpty && asyncBindings.IsDefaultOrEmpty)
            return;

        var template = new ValidatorRegistrationTemplate(validValidators, asyncBindings);
        var artifact = template.RenderOutput();

        context.AddSource(artifact);
    }

    #endregion
}
