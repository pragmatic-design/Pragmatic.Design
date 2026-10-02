using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Generates a per-assembly aggregate DI registration for all mutation invokers.
/// </summary>
internal sealed class MutationRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<MutationModel> _mutations;
    private readonly string _namespacePrefix;
    private readonly string? _permissionRegistryNamespace;
    private readonly string? _policyRegistryNamespace;

    /// <param name="mutations">Mutations whose invokers this assembly registers.</param>
    /// <param name="permissionRegistryNamespace">
    ///     Namespace of the generated permission registry to register here, or <c>null</c>.
    ///     Only set when the assembly has NO actions (otherwise ActionsRegistrationTemplate registers
    ///     it) — avoids a duplicate registration.
    /// </param>
    /// <param name="policyRegistryNamespace">Namespace of the generated policy registry, or <c>null</c>.</param>
    public MutationRegistrationTemplate(
        ImmutableArray<MutationModel> mutations,
        string? permissionRegistryNamespace = null,
        string? policyRegistryNamespace = null)
    {
        _mutations = mutations;
        _namespacePrefix = DeriveNamespacePrefix(mutations);
        _permissionRegistryNamespace = permissionRegistryNamespace;
        _policyRegistryNamespace = policyRegistryNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.Actions";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Mutations", "Registration"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return !_mutations.IsEmpty;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(GeneratedRegistrationNames.RegistrationNamespace(_namespacePrefix));
        AppendLine();

        var className =
            NamespacePrefixHelper.ToIdentifier(_namespacePrefix) + GeneratedRegistrationNames.MutationsClassSuffix;

        XmlSummary("Extension methods for registering all Mutation invokers in this assembly.");

        Class(className, RenderClassBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    private void RenderClassBody()
    {
        var methodName = "Add" + NamespacePrefixHelper.ToIdentifier(_namespacePrefix) + "Mutations";

        XmlSummary("Registers all Mutation invokers from this assembly.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        Method(methodName, RenderRegistrationBody,
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            new List<MethodParameter>
            {
                new("global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services")
                {
                    IsExtension = true
                }
            },
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     The compensators declared by mutations in this assembly, and the scope that holds their undos.
    /// </summary>
    /// <remarks>
    ///     <c>TryAdd</c> throughout: the action registration in the same assembly emits the same scope,
    ///     and two modules may each declare compensators.
    /// </remarks>
    private void RenderCompensationRegistrations()
    {
        var compensators = _mutations
            .Where(m => m.CompensatorTypeName is not null)
            .Select(m => m.CompensatorTypeName!)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (compensators.Count == 0)
            return;

        AppendLine();
        Comment("Declared compensators + the request scope that holds their undos");
        AppendLine(
            "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<" +
            "global::Pragmatic.Actions.Compensation.ICompensationScope, " +
            "global::Pragmatic.Actions.Compensation.CompensationScope>(services);");

        foreach (var compensator in compensators)
        {
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<" +
                $"{compensator}>(services);");
        }
    }

    /// <summary>
    ///     The preset providers declared by <c>[PresetProvider&lt;T&gt;]</c> on the entities these
    ///     mutations create.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generated invoker resolves each provider with <c>GetRequiredService</c>, so with
    ///         nothing registering them, declaring the attribute would make <b>every</b> creation of that
    ///         entity answer 500 with "No service for type … has been registered", not only one that
    ///         wanted a preset. The provider type is known here at compile time, so leaving the
    ///         registration to the application buys nothing and costs a runtime failure with no
    ///         diagnostic.
    ///     </para>
    ///     <para>
    ///         Scoped, and <c>TryAdd</c>, for the same reasons as the compensators above: a provider may
    ///         touch a DbContext, and two mutations on the same entity would otherwise register it twice.
    ///         An application that wants a different lifetime, or a provider with constructor arguments
    ///         the container cannot supply, registers it first and this line stands aside.
    ///     </para>
    /// </remarks>
    private void RenderPresetProviderRegistrations()
    {
        var providers = _mutations
            .Where(m => m.Presets is not null)
            .SelectMany(m => m.Presets!.Providers)
            .Select(p => p.ProviderTypeFqn)
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (providers.Count == 0)
            return;

        AppendLine();
        Comment("Preset providers declared by [PresetProvider<T>] on the entities these mutations create");

        foreach (var provider in providers)
        {
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<" +
                $"{provider}>(services);");
        }
    }

    /// <summary>
    ///     The <c>[ResiliencePolicy]</c> names this assembly's mutations ask for, declared so the host can
    ///     check at startup that each one is defined — the mutations' half of
    ///     <see cref="ActionsRegistrationTemplate" />'s declarations.
    /// </summary>
    private void RenderResiliencePolicyDeclarations()
    {
        var declared = _mutations
            .Where(m => m.HasResilience)
            .OrderBy(m => m.FullQualifiedName, StringComparer.Ordinal)
            .ToList();

        if (declared.Count == 0)
            return;

        AppendLine();
        Comment("[ResiliencePolicy] names, checked at startup against what the configuration defines");
        foreach (var mutation in declared)
        {
            var operation = string.IsNullOrEmpty(mutation.Namespace)
                ? mutation.TypeName
                : $"{mutation.Namespace}.{mutation.TypeName}";

            AppendLine(
                "services.AddSingleton(new global::Pragmatic.Resilience.DeclaredResiliencePolicy(" +
                $"\"{StringHelper.CSharpLiteral(mutation.Resilience!.PolicyName)}\", \"{StringHelper.CSharpLiteral(operation)}\"));");
        }
    }

    private void RenderRegistrationBody()
    {
        foreach (var mutation in _mutations.OrderBy(m => m.FullQualifiedName))
        {
            var mutationType = string.IsNullOrEmpty(mutation.Namespace)
                ? mutation.TypeName
                : $"global::{mutation.Namespace}.{mutation.TypeName}";

            var invokerType = $"{mutationType}.Invoker";
            var interfaceType =
                $"global::Pragmatic.Actions.Invoker.IMutationInvoker<{mutationType}, {mutation.EntityFullTypeName}>";

            AppendLine($"services.AddScoped<{interfaceType}, {invokerType}>();");
        }

        RenderCompensationRegistrations();
        RenderPresetProviderRegistrations();
        RenderResiliencePolicyDeclarations();

        // Auto-register IDefaultValueGenerator<TEntity, TValue> from [ComputedDefault] and auto-generatable
        // [GeneratedValue] attributes. Sequence-backed formatters inject a keyed DbContext, so they register
        // as scoped; stateless generators stay singleton.
        var generators = _mutations
            .Where(m => m.HasComputedDefaults)
            .SelectMany(m => m.ComputedDefaults!.Properties)
            .Select(p => (p.EntityTypeFqn, p.ValueTypeFqn, p.GeneratorTypeFqn, p.RequiresScope))
            .Distinct()
            .ToList();

        if (generators.Count > 0)
        {
            AppendLine();
            Comment("Auto-registered IDefaultValueGenerator from [ComputedDefault]/[GeneratedValue] attributes");
            foreach (var (entityFqn, valueFqn, generatorFqn, requiresScope) in generators)
            {
                var interfaceFqn = $"global::Pragmatic.Persistence.Lifecycle.IDefaultValueGenerator<{entityFqn}, {valueFqn}>";
                var lifetime = requiresScope ? "AddScoped" : "AddSingleton";
                AppendLine($"services.{lifetime}<{interfaceFqn}, {generatorFqn}>();");
            }
        }

        // Register the generated authorization registries (only when this assembly has no
        // actions — otherwise ActionsRegistrationTemplate already does, avoiding a duplicate).
        if (_permissionRegistryNamespace is not null)
            AppendLine(
                $"services.AddSingleton<global::Pragmatic.Actions.Pipeline.IPermissionRequirementRegistry, {Fqn(_permissionRegistryNamespace, "GeneratedPermissionRequirementRegistry")}>();");

        if (_policyRegistryNamespace is not null)
            AppendLine(
                $"services.AddSingleton<global::Pragmatic.Actions.Pipeline.IPolicyRegistry, {Fqn(_policyRegistryNamespace, "GeneratedPolicyRegistry")}>();");

        AppendLine();
        AppendLine("return services;");
    }

    private static string Fqn(string ns, string typeName)
        => string.IsNullOrEmpty(ns) ? $"global::{typeName}" : $"global::{ns}.{typeName}";

    private static string DeriveNamespacePrefix(ImmutableArray<MutationModel> mutations)
        => NamespacePrefixHelper.DerivePrefix(mutations.Select(m => m.Namespace));

    /// <summary>
    ///     The <c>Namespace.Class.Method</c> a host calls to register this assembly's mutation invokers,
    ///     or <c>null</c> when there are none and no file is emitted.
    /// </summary>
    /// <remarks>
    ///     The mutations counterpart of
    ///     <see cref="ActionsRegistrationTemplate.RegistrationMethodFor" />: an assembly can have one,
    ///     the other, or both, and a module whose operations are all mutations is an ordinary shape.
    /// </remarks>
    public static string? RegistrationMethodFor(ImmutableArray<MutationModel> mutations)
        => mutations.IsDefaultOrEmpty
            ? null
            : GeneratedRegistrationNames.MutationsFqn(DeriveNamespacePrefix(mutations));
}
