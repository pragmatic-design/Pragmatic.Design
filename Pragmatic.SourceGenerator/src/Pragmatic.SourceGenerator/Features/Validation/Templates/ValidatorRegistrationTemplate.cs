using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Templates;

/// <summary>
///     Template for generating DI registration extension method for validators
///     and async validator bindings.
/// </summary>
internal sealed class ValidatorRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<AsyncValidatorBindingsModel> _asyncBindings;
    private readonly string _namespacePrefix;
    private readonly ImmutableArray<ValidatorModel> _validators;

    public ValidatorRegistrationTemplate(
        ImmutableArray<ValidatorModel> validators,
        ImmutableArray<AsyncValidatorBindingsModel> asyncBindings)
    {
        _validators = validators;
        _asyncBindings = asyncBindings;
        _namespacePrefix = NamespacePrefixFor(validators, asyncBindings);
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Validation";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForAssembly("Validation", "ValidatorRegistration"),
        ToSourceText());

    /// <summary>
    ///     The <c>Namespace.Class.Method</c> the host calls for these validators. Whoever tells the
    ///     host about them — the metadata attribute, or the local-registration channel a host-declared
    ///     <c>[Validator]</c> travels through — reads it from here, so it cannot drift from what this
    ///     template renders.
    /// </summary>
    public static string FqnFor(
        ImmutableArray<ValidatorModel> validators,
        ImmutableArray<AsyncValidatorBindingsModel> asyncBindings)
        => GeneratedRegistrationNames.ValidatorsFqn(NamespacePrefixFor(validators, asyncBindings));

    /// <summary>The namespace this assembly's validator registration lands in.</summary>
    internal static string NamespacePrefixFor(
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

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");
        AddUsing("Pragmatic.Validation");
        AddUsing("Pragmatic.Validation.Extensions");

        if (!_validators.IsDefaultOrEmpty)
            foreach (var validator in _validators)
            {
                if (!string.IsNullOrEmpty(validator.Namespace))
                    AddUsing(validator.Namespace);

                if (!string.IsNullOrEmpty(validator.ValidatedType.Namespace) &&
                    validator.ValidatedType.Namespace != validator.Namespace)
                    AddUsing(validator.ValidatedType.Namespace);
            }

        if (!_asyncBindings.IsDefaultOrEmpty)
            foreach (var binding in _asyncBindings)
            {
                if (!string.IsNullOrEmpty(binding.Namespace))
                    AddUsing(binding.Namespace);
            }

        AppendLine();
        AppendNamespace(_namespacePrefix);
        AppendLine();

        var modifiers = new ClassModifiers { Partial = true, IsStatic = true };
        Class(GeneratedRegistrationNames.ValidatorsClass, RenderBody,
            accessModifier: AccessModifier.Public, modifiers: modifiers);
    }

    private void RenderBody()
    {
        RenderRegisterMethod();
    }

    private void RenderRegisterMethod()
    {
        XmlSummary("Registers all discovered validators, bindings, and their composite validators.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("IServiceCollection", "services") { IsExtension = true }
        };

        var modifiers = new MethodModifiers { IsStatic = true };

        Method(GeneratedRegistrationNames.ValidatorsMethod, RenderRegistrations, "IServiceCollection",
            parameters, modifiers: modifiers);
    }

    private void RenderRegistrations()
    {
        if (!_asyncBindings.IsDefaultOrEmpty)
            foreach (var binding in _asyncBindings)
            {
                RenderAsyncBindingsRegistration(binding);
                AppendLine();
            }

        if (!_validators.IsDefaultOrEmpty)
            foreach (var validator in _validators)
            {
                RenderValidatorRegistration(validator);
                AppendLine();
            }

        // Register CompositeValidator for entities that have bindings but NO [Validator] class
        if (!_asyncBindings.IsDefaultOrEmpty)
        {
            var validatorTargetTypes = _validators.IsDefaultOrEmpty
                ? new HashSet<string>()
                : new HashSet<string>(_validators.Select(v => v.ValidatedType.FullName));

            foreach (var binding in _asyncBindings)
            {
                if (validatorTargetTypes.Contains(binding.FullTypeName))
                    continue;

                Comment($"CompositeValidator for {binding.TypeName} (async bindings only)");
                AppendLine(
                    $"services.AddSyncOnlyValidator<{binding.TypeName}>();");
                AppendLine();
            }
        }

        Return("services");
    }

    private void RenderAsyncBindingsRegistration(AsyncValidatorBindingsModel binding)
    {
        Comment($"Async validator bindings for {binding.TypeName}");
        AppendLine(
            $"services.AddAsyncValidatorBindings<{binding.TypeName}AsyncValidatorBindings, {binding.TypeName}>();");
    }

    private void RenderValidatorRegistration(ValidatorModel validator)
    {
        var lifetime = GetLifetimeMethod(validator.Lifetime);

        Comment($"Register {validator.ValidatorTypeName} for {validator.ValidatedType.TypeName}");

        if (validator.ValidatedTypeHasSyncValidation)
        {
            AppendLine(
                $"services.AddValidatorWithComposite<{validator.ValidatorFullName}, {validator.ValidatedType.FullName}>(global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{lifetime});");
        }
        else
        {
            AppendLine(
                $"services.AddAsyncValidator<{validator.ValidatorFullName}, {validator.ValidatedType.FullName}>(global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{lifetime});");
            AppendLine(
                $"services.AddSyncOnlyValidator<{validator.ValidatedType.FullName}>(global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{lifetime});");
        }
    }

    private static string GetLifetimeMethod(ServiceLifetimeKind lifetime)
    {
        return lifetime switch
        {
            ServiceLifetimeKind.Singleton => "Singleton",
            ServiceLifetimeKind.Scoped => "Scoped",
            ServiceLifetimeKind.Transient => "Transient",
            _ => "Scoped"
        };
    }
}
