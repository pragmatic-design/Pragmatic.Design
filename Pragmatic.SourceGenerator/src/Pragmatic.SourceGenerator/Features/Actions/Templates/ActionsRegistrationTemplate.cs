using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Generates a per-assembly aggregate DI registration extension method.
/// </summary>
internal sealed class ActionsRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<ActionModel> _actions;
    private readonly string _namespacePrefix;
    private readonly string? _permissionRegistryNamespace;
    private readonly string? _policyRegistryNamespace;

    /// <param name="actions">Actions whose invokers this assembly registers.</param>
    /// <param name="permissionRegistryNamespace">
    ///     Namespace of the generated permission registry, or <c>null</c> if none was generated.
    ///     The standalone path must register the generated authorization registries —
    ///     otherwise <c>AddPragmaticActions()</c>'s empty defaults win and [RequirePermission]/
    ///     [RequirePolicy] silently fail open.
    /// </param>
    /// <param name="policyRegistryNamespace">Namespace of the generated policy registry, or <c>null</c>.</param>
    public ActionsRegistrationTemplate(
        ImmutableArray<ActionModel> actions,
        string? permissionRegistryNamespace = null,
        string? policyRegistryNamespace = null)
    {
        _actions = actions;
        _namespacePrefix = DeriveNamespacePrefix(actions);
        _permissionRegistryNamespace = permissionRegistryNamespace;
        _policyRegistryNamespace = policyRegistryNamespace;
    }

    protected override string? GeneratorName => "Pragmatic.Actions";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Actions", "Registration"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return !_actions.IsEmpty;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");

        AppendNamespace(GeneratedRegistrationNames.RegistrationNamespace(_namespacePrefix));
        AppendLine();

        var className =
            NamespacePrefixHelper.ToIdentifier(_namespacePrefix) + GeneratedRegistrationNames.ActionsClassSuffix;

        XmlSummary("Extension methods for registering all DomainAction invokers in this assembly.");

        Class(className, RenderClassBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    /// <summary>
    ///     The <c>Namespace.Class.Method</c> a host calls to register this assembly's action invokers,
    ///     or <c>null</c> when there are none and no file is emitted.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Read by the Actions metadata so the host <em>calls</em> this method instead of writing the
    ///     same registrations again. It has to agree with <see cref="RenderFile" /> exactly, which is why
    ///     both go through <see cref="GeneratedRegistrationNames" /> rather than composing the name twice.
    /// </remarks>
    public static string? RegistrationMethodFor(ImmutableArray<ActionModel> actions)
        => actions.IsDefaultOrEmpty
            ? null
            : GeneratedRegistrationNames.ActionsFqn(DeriveNamespacePrefix(actions));

    private void RenderClassBody()
    {
        var methodName = "Add" + NamespacePrefixHelper.ToIdentifier(_namespacePrefix) + "Actions";

        XmlSummary("Registers all DomainAction invokers from this assembly.");
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

    private void RenderRegistrationBody()
    {
        foreach (var action in _actions.OrderBy(a => a.FullQualifiedName))
        {
            var actionType = string.IsNullOrEmpty(action.Namespace)
                ? action.TypeName
                : $"global::{action.Namespace}.{action.TypeName}";

            var invokerType = $"{actionType}.Invoker";

            var invokerInterface = action.IsVoid
                ? $"global::Pragmatic.Actions.Invoker.IVoidDomainActionInvoker<{actionType}>"
                : $"global::Pragmatic.Actions.Invoker.IDomainActionInvoker<{actionType}, {action.ReturnTypeName ?? "object"}>";

            AppendLine($"services.AddScoped<{invokerInterface}, {invokerType}>();");
        }

        // Composite actions with mutation-step properties: register the nested
        // CompositeInvoker (injected by the action's Invoker) and the concrete step invokers it needs.
        RenderCompositeRegistrations();

        // Register the generated authorization registries (AddSingleton wins over the
        // empty TryAddSingleton defaults in AddPragmaticActions, regardless of call order).
        RenderAuthorizationRegistries();

        RenderCompensationRegistrations();

        RenderResiliencePolicyDeclarations();

        AppendLine();
        AppendLine("return services;");
    }

    /// <summary>
    ///     The <c>[ResiliencePolicy]</c> names this assembly's actions ask for, declared so the host can
    ///     check at startup that each one is defined.
    /// </summary>
    /// <remarks>
    ///     A fact about the module, true on its own: the module says which names it uses, the host —
    ///     which is the one that sees the configuration — says whether they exist. Without it a name
    ///     nothing defines ran as a passthrough with no word about it.
    /// </remarks>
    private void RenderResiliencePolicyDeclarations()
    {
        var declared = _actions
            .Where(a => a.HasResilience)
            .OrderBy(a => a.FullQualifiedName, StringComparer.Ordinal)
            .ToList();

        if (declared.Count == 0)
            return;

        AppendLine();
        Comment("[ResiliencePolicy] names, checked at startup against what the configuration defines");
        foreach (var action in declared)
        {
            var operation = string.IsNullOrEmpty(action.Namespace)
                ? action.TypeName
                : $"{action.Namespace}.{action.TypeName}";

            AppendLine(
                "services.AddSingleton(new global::Pragmatic.Resilience.DeclaredResiliencePolicy(" +
                $"\"{StringHelper.CSharpLiteral(action.Resilience!.PolicyName)}\", \"{StringHelper.CSharpLiteral(operation)}\"));");
        }
    }

    private void RenderCompositeRegistrations()
    {
        var composites = _actions
            .Where(a => a.HasCompositeSteps)
            .OrderBy(a => a.FullQualifiedName)
            .ToList();

        if (composites.Count == 0)
            return;

        AppendLine();
        Comment("Composite invokers + their concrete step invokers");
        foreach (var action in composites)
        {
            var actionType = string.IsNullOrEmpty(action.Namespace)
                ? action.TypeName
                : $"global::{action.Namespace}.{action.TypeName}";

            AppendLine($"services.AddScoped<{actionType}.CompositeInvoker>();");
        }

        // Concrete step invokers — distinct (a mutation may be a step of several composites, or
        // appear twice in one composite).
        var stepInvokers = composites
            .SelectMany(a => a.CompositeStepInvokerTypes)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal);

        foreach (var stepInvoker in stepInvokers)
            AppendLine($"services.AddScoped<{stepInvoker}>();");
    }

    /// <summary>
    ///     The compensators declared in this assembly, and the scope that holds their undos.
    /// </summary>
    /// <remarks>
    ///     Emitted only when something declares <c>[UndoWith&lt;T&gt;]</c>. An application with no
    ///     compensators registers no scope, and every invoker's compensation branch resolves <c>null</c>
    ///     and does nothing — the mechanism costs an application that does not use it exactly zero.
    ///     <c>TryAdd</c> because two modules may each declare compensators and both register the scope.
    /// </remarks>
    private void RenderCompensationRegistrations()
    {
        var compensators = _actions
            .Where(a => a.CompensatorTypeName is not null)
            .Select(a => a.CompensatorTypeName!)
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

    private void RenderAuthorizationRegistries()
    {
        if (_permissionRegistryNamespace is not null)
            AppendLine(
                $"services.AddSingleton<global::Pragmatic.Actions.Pipeline.IPermissionRequirementRegistry, {Fqn(_permissionRegistryNamespace, "GeneratedPermissionRequirementRegistry")}>();");

        if (_policyRegistryNamespace is not null)
            AppendLine(
                $"services.AddSingleton<global::Pragmatic.Actions.Pipeline.IPolicyRegistry, {Fqn(_policyRegistryNamespace, "GeneratedPolicyRegistry")}>();");
    }

    private static string Fqn(string ns, string typeName)
        => string.IsNullOrEmpty(ns) ? $"global::{typeName}" : $"global::{ns}.{typeName}";

    private static string DeriveNamespacePrefix(ImmutableArray<ActionModel> actions)
        => NamespacePrefixHelper.DerivePrefix(actions.Select(a => a.Namespace));

}
