// Pragmatic.SourceGenerator - Composition - Pragmatic Host Template (Domain Actions)

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Domain action registration: DomainAction invokers, Mutation invokers, and boundary interfaces.
/// </summary>
internal sealed partial class PragmaticHostTemplate
{
    private void RenderRegisterAllDomainActionsMethod()
    {
        XmlSummary("Registers all DomainAction invokers, Mutation invokers, and boundary interfaces from discovered modules.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IServiceCollection", "services")
        };

        Method("RegisterAllDomainActions", () =>
        {
            if (!HasDomainModules && !HasDiscoveredActions)
            {
                Comment("No domain modules discovered");
                AppendLine("return services;");
                return;
            }

            Comment("Core action pipeline (filters: validation, logging, resilience, permissions)");
            AddUsing("Pragmatic.Actions.Extensions");
            AppendLine("services.AddPragmaticActions();");
            AppendLine();

            // Build set of remote boundary assembly names (skip their boundary extensions)
            var remoteAssemblySet = _model.HasRemoteBoundaries
                ? new HashSet<string>(
                    _model.RemoteBoundaries
                        .Where(rb => rb.AssemblyName is not null)
                        .Select(rb => rb.AssemblyName!),
                    StringComparer.Ordinal)
                : new HashSet<string>();

            // Boundary interfaces (convention-derived extension method call)
            // Call boundary extensions for modules that have actions OR use packages
            // (package-fused actions are registered by the boundary extension).
            // Skip boundary extensions for remote modules.
            if (HasDomainModules)
            {
                // Collect assemblies that have operations (from metadata)
                var assembliesWithActions = new HashSet<string>();
                if (!_model.DiscoveredActions.IsDefaultOrEmpty)
                    foreach (var a in _model.DiscoveredActions)
                        assembliesWithActions.Add(a.SourceAssembly);

                // ⚠️ Mutations count too. Reading only DiscoveredActions meant a module whose operations
                // are all mutations — an ordinary shape — got its boundary interface generated, its
                // registration extension emitted, and never called: injecting I{Module}Actions from
                // another module failed at container validation. Conformance.Catalog was exactly that.
                if (!_model.DiscoveredMutations.IsDefaultOrEmpty)
                    foreach (var m in _model.DiscoveredMutations)
                        assembliesWithActions.Add(m.SourceAssembly);

                // Also include assemblies that use packages — their boundary extensions
                // register the package invokers (e.g., Accounts uses Identity.Local)
                //
                // ⚠️ Only the ones this host hosts. The Discovered* lists above arrive filtered by
                // [Include<T>]; DiscoveredModules does not, so this branch was reaching past the
                // filter — and a boundary extension registers its package's invokers without their
                // stores. The standalone Billing host hosts Billing alone, and Booking's reference to
                // Accounts was enough to make it call AddAccountsBoundary and fail container
                // validation before any request.
                var included = _model.IncludedAssemblyNames.IsDefaultOrEmpty
                    ? null
                    : new HashSet<string>(_model.IncludedAssemblyNames.AsImmutableArray(), StringComparer.Ordinal);

                if (!_model.DiscoveredModules.IsDefaultOrEmpty)
                    foreach (var module in _model.DiscoveredModules)
                        if (!module.PackageAssemblyNames.IsDefaultOrEmpty && !string.IsNullOrEmpty(module.AssemblyName)
                            && (included is null || included.Contains(module.AssemblyName)))
                            assembliesWithActions.Add(module.AssemblyName);
                if (!_model.LocalModules.IsDefaultOrEmpty)
                    foreach (var module in _model.LocalModules)
                        if (!module.UsePackages.IsDefaultOrEmpty && !string.IsNullOrEmpty(module.AssemblyName))
                            assembliesWithActions.Add(module.AssemblyName!);

                var hasAnyBoundary = false;
                foreach (var module in _model.AllDomainModules.OrderBy(m => m.Name))
                {
                    if (string.IsNullOrEmpty(module.BoundaryTypeName))
                        continue;

                    // Skip boundaries without actions and without packages
                    if (!assembliesWithActions.Contains(module.AssemblyName))
                        continue;

                    if (!hasAnyBoundary)
                    {
                        Comment("Boundary interfaces");
                        hasAnyBoundary = true;
                    }

                    var boundaryNs = ExtractNamespaceFromBoundaryType(module.BoundaryTypeName!);
                    var moduleName = module.Name;
                    var isRemote = remoteAssemblySet.Contains(module.AssemblyName);

                    if (isRemote)
                    {
                        // Remote boundary — use BoundaryMode.Remote (delegates to RemoteActions via HTTP)
                        AppendLine(
                            $"global::{boundaryNs}.{moduleName}BoundaryExtensions.Add{moduleName}Boundary(services, global::Pragmatic.Actions.Boundary.BoundaryMode.Remote);");
                    }
                    else
                    {
                        // Local boundary — default mode
                        AppendLine(
                            $"global::{boundaryNs}.{moduleName}BoundaryExtensions.Add{moduleName}Boundary(services);");
                    }
                }

                if (hasAnyBoundary)
                    AppendLine();
            }

            // Skip package assembly invokers — they are registered by boundary extensions
            // Skip remote assembly invokers — they use HTTP invokers instead
            var packageAssemblySet = _model.PackageAssemblyNames.IsDefaultOrEmpty
                ? new HashSet<string>()
                : new HashSet<string>(_model.PackageAssemblyNames, StringComparer.Ordinal);

            // Modules that generate their own invoker registrations declare the entry points in their
            // Actions metadata. The host calls those, the way it already calls a module's boundary
            // extension, its reads registration and its redaction map — invokers were the exception.
            // ⚠️ The point is not the duplication, which worked: both copies register the same pairs.
            // It is that writing the lines here means naming types from another assembly, so every
            // generated invoker had to be public even when its dependencies are internal.
            var selfRegistering = CollectSelfRegisteringAssemblies(packageAssemblySet, remoteAssemblySet);
            RenderModuleInvokerRegistrations(selfRegistering);

            if (!_model.DiscoveredActions.IsDefaultOrEmpty)
            {
                var grouped = _model.DiscoveredActions
                    .Where(a => !packageAssemblySet.Contains(a.SourceAssembly) &&
                                !remoteAssemblySet.Contains(a.SourceAssembly) &&
                                !selfRegistering.ContainsKey(a.SourceAssembly))
                    .GroupBy(a => a.SourceAssembly)
                    .OrderBy(g => g.Key);

                foreach (var group in grouped)
                {
                    Comment($"Action invokers from {group.Key}");
                    foreach (var action in group.OrderBy(a => a.ActionType))
                    {
                        if (action.IsVoid)
                        {
                            AppendLine(
                                $"services.AddScoped<global::Pragmatic.Actions.Invoker.IVoidDomainActionInvoker<global::{action.ActionType}>, {action.InvokerType}>();");
                        }
                        else
                        {
                            var returnType = action.ReturnType ?? "object";
                            AppendLine(
                                $"services.AddScoped<global::Pragmatic.Actions.Invoker.IDomainActionInvoker<global::{action.ActionType}, {returnType}>, {action.InvokerType}>();");
                        }
                    }
                }

                AppendLine();

                // Composite invokers + their concrete step invokers (host parity with the
                // standalone ActionsRegistrationTemplate). The action's Invoker injects CompositeInvoker.
                var composites = _model.DiscoveredActions
                    .Where(a => a.HasCompositeSteps &&
                                !packageAssemblySet.Contains(a.SourceAssembly) &&
                                !remoteAssemblySet.Contains(a.SourceAssembly) &&
                                !selfRegistering.ContainsKey(a.SourceAssembly))
                    .OrderBy(a => a.ActionType)
                    .ToList();

                if (composites.Count > 0)
                {
                    Comment("Composite invokers + concrete step invokers");
                    foreach (var action in composites)
                        AppendLine($"services.AddScoped<global::{action.ActionType}.CompositeInvoker>();");

                    // Concrete step invokers — distinct across all composites.
                    var stepInvokers = composites
                        .SelectMany(a => a.CompositeStepInvokers)
                        .Distinct()
                        .OrderBy(s => s, StringComparer.Ordinal);

                    foreach (var stepInvoker in stepInvokers)
                        AppendLine($"services.AddScoped<{stepInvoker}>();");

                    AppendLine();
                }
            }

            // Remote boundary actions are now registered via AddXxxBoundary(BoundaryMode.Remote)
            // which delegates to the module's RemoteActions implementation (generated in _Boundary.Xxx.Remote.g.cs)

            // Mutation invokers (direct DI from enriched metadata)
            // Skip package assembly mutations — they are registered by boundary extensions
            // Skip remote assembly mutations — not supported for remote boundaries in MVP
            if (!_model.DiscoveredMutations.IsDefaultOrEmpty)
            {
                var grouped = _model.DiscoveredMutations
                    .Where(m => !packageAssemblySet.Contains(m.SourceAssembly) &&
                                !remoteAssemblySet.Contains(m.SourceAssembly) &&
                                !selfRegistering.ContainsKey(m.SourceAssembly))
                    .GroupBy(m => m.SourceAssembly)
                    .OrderBy(g => g.Key);

                foreach (var group in grouped)
                {
                    Comment($"Mutation invokers from {group.Key}");
                    foreach (var mutation in group.OrderBy(m => m.MutationType))
                        AppendLine(
                            $"services.AddScoped<global::Pragmatic.Actions.Invoker.IMutationInvoker<global::{mutation.MutationType}, {mutation.EntityType}>, {mutation.InvokerType}>();");
                }

                AppendLine();
            }

            // ⚠️ After the module registrations, not before. Each module's own registration extension
            // registers a policy and permission registry covering **that module only**, and both are
            // injected singly — last registration wins. Registered first, the host's aggregate would be
            // shadowed by whichever module the host happened to call last, and every other module's
            // [RequirePermission] would resolve against a registry that has never heard of it.
            // They still displace AddPragmaticActions()'s throwing defaults: those go in with
            // TryAddSingleton, which never overwrites and never wins a later AddSingleton.
            RenderPolicyAndPermissionRegistries();

            // Register IDefaultValueGenerator<TEntity, TValue> from [ComputedDefault] and auto-generatable
            // [GeneratedValue] attributes. Sequence-backed formatters inject a keyed DbContext, so they
            // register as scoped; stateless generators stay singleton.
            // ⚠️ Remote assemblies excluded here too, and it had to be said three times because these
            // three lists are read straight from the model rather than from the filtered groupings
            // above. A sequence-backed generator injects a keyed DbContext, and a module this host
            // reaches over HTTP has no database here to key — which is what the distributed host's
            // container validation reported second, once it had something to start.
            var allComputedDefaults = _model.DiscoveredMutations
                .Where(m => !remoteAssemblySet.Contains(m.SourceAssembly))
                .Where(m => !m.ComputedDefaults.IsDefaultOrEmpty)
                .SelectMany(m => m.ComputedDefaults)
                .GroupBy(cd => (cd.EntityTypeFqn, cd.ValueTypeFqn, cd.GeneratorTypeFqn))
                .Select(g => g.First())
                .ToList();

            if (allComputedDefaults.Count > 0)
            {
                Comment("IDefaultValueGenerator from [ComputedDefault]/[GeneratedValue] attributes");
                foreach (var cd in allComputedDefaults)
                {
                    var interfaceFqn =
                        $"global::Pragmatic.Persistence.Lifecycle.IDefaultValueGenerator<{cd.EntityTypeFqn}, {cd.ValueTypeFqn}>";
                    var lifetime = cd.RequiresScope ? "AddScoped" : "AddSingleton";
                    AppendLine($"services.{lifetime}<{interfaceFqn}, {cd.GeneratorTypeFqn}>();");
                }

                AppendLine();
            }

            RenderCompensatorRegistrations(remoteAssemblySet);
            RenderPresetProviderRegistrations(remoteAssemblySet);

            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     Registers the compensators declared by <c>[UndoWith&lt;T&gt;]</c> and the request scope that
    ///     holds their undos.
    /// </summary>
    /// <remarks>
    ///     It has to happen here, not only in the module's own <c>Add*Actions</c> extension: an
    ///     application never calls that extension. The host rebuilds the whole registration list from
    ///     the metadata channel, so a registration emitted only in the module is a producer with no
    ///     caller — the compensator would resolve to nothing and the scope would be absent, leaving the
    ///     mechanism silently inert. Measured on a real application before this existed.
    /// </remarks>
    /// <summary>
    ///     Registers the preset providers the generated mutation invokers resolve.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Here as well as in the module's own registration, and for the same reason the compensators
    ///     are in both: the host writes its container directly from the discovered manifest and never
    ///     calls <c>Add{Module}Mutations</c>. Registering only in the module left the provider unknown to
    ///     the container that actually runs, and every creation of the entity answered 500 with "No
    ///     service for type … has been registered" — not only one that wanted a preset.
    /// </remarks>
    /// <summary>
    ///     Assembly name → the registration methods it declares, for the assemblies whose invokers the
    ///     host must not write out itself.
    /// </summary>
    /// <remarks>
    ///     Applies the same exclusions as the per-invoker emission it replaces — package assemblies are
    ///     registered by their boundary extension, remote ones use HTTP invokers — because a set that
    ///     does not match exactly is a registration that silently disappears.
    /// </remarks>
    private Dictionary<string, (string Actions, string Mutations)> CollectSelfRegisteringAssemblies(
        HashSet<string> packageAssemblySet,
        HashSet<string> remoteAssemblySet)
    {
        var found = new Dictionary<string, (string Actions, string Mutations)>(StringComparer.Ordinal);

        if (_model.Assemblies.IsDefaultOrEmpty)
            return found;

        foreach (var assembly in _model.Assemblies)
        {
            if (packageAssemblySet.Contains(assembly.AssemblyName) ||
                remoteAssemblySet.Contains(assembly.AssemblyName))
                continue;

            foreach (var entry in assembly.Entries)
            {
                if (entry.Category != MetadataCategoryIds.Actions)
                    continue;

                if (string.IsNullOrEmpty(entry.RegistrationMethod) &&
                    string.IsNullOrEmpty(entry.SecondaryRegistrationMethod))
                    continue;

                found[assembly.AssemblyName] = (entry.RegistrationMethod, entry.SecondaryRegistrationMethod);
            }
        }

        return found;
    }

    /// <summary>Calls each module's generated registration extension, in a stable order.</summary>
    private void RenderModuleInvokerRegistrations(
        Dictionary<string, (string Actions, string Mutations)> selfRegistering)
    {
        if (selfRegistering.Count == 0)
            return;

        Comment("Invokers — each module registers its own");

        foreach (var pair in selfRegistering.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrEmpty(pair.Value.Actions))
                AppendLine($"global::{pair.Value.Actions}(services);");

            if (!string.IsNullOrEmpty(pair.Value.Mutations))
                AppendLine($"global::{pair.Value.Mutations}(services);");
        }

        AppendLine();
    }

    private void RenderPresetProviderRegistrations(HashSet<string> remoteAssemblySet)
    {
        var providers = _model.DiscoveredMutations
            .Where(m => !remoteAssemblySet.Contains(m.SourceAssembly))
            .SelectMany(m => m.PresetProviders.AsImmutableArray())
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (providers.Count == 0)
            return;

        Comment("Preset providers declared by [PresetProvider<T>] on the entities these mutations create");

        const string tryAdd =
            "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped";

        foreach (var provider in providers)
            AppendLine($"{tryAdd}<{provider}>(services);");

        AppendLine();
    }

    private void RenderCompensatorRegistrations(HashSet<string> remoteAssemblySet)
    {
        var compensators = _model.DiscoveredActions
            .Where(a => !remoteAssemblySet.Contains(a.SourceAssembly))
            .Select(a => a.CompensatorType)
            .Concat(_model.DiscoveredMutations
                .Where(m => !remoteAssemblySet.Contains(m.SourceAssembly))
                .Select(m => m.CompensatorType))
            .Where(c => c is not null)
            .Select(c => c!)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (compensators.Count == 0)
            return;

        Comment("Declared compensators + the request scope that holds their undos ([UndoWith<T>])");
        // Called through the static class rather than as an extension: the generated host does not
        // import Microsoft.Extensions.DependencyInjection.Extensions, and relying on a using this file
        // cannot see emits code that reads correctly and does not compile.
        const string tryAdd =
            "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped";

        AppendLine(
            $"{tryAdd}<global::Pragmatic.Actions.Compensation.ICompensationScope, " +
            "global::Pragmatic.Actions.Compensation.CompensationScope>(services);");

        foreach (var compensator in compensators)
            AppendLine($"{tryAdd}<{compensator}>(services);");

        AppendLine();
    }

    /// <summary>
    ///     Generates inline policy and permission requirement registries from discovered action metadata.
    ///     These MUST be registered before AddPragmaticActions() because it uses TryAddSingleton
    ///     which won't overwrite existing registrations.
    /// </summary>
    private void RenderPolicyAndPermissionRegistries()
    {
        // Collect policy entries from all discovered actions and mutations
        var policyEntries = new List<(string ActionType, string PolicyType)>();
        var permissionEntries = new List<(string ActionType, System.Collections.Immutable.ImmutableArray<string> Permissions, bool RequireAll)>();

        if (!_model.DiscoveredActions.IsDefaultOrEmpty)
        {
            foreach (var action in _model.DiscoveredActions)
            {
                if (action.PolicyTypeFqn is not null)
                    policyEntries.Add((action.ActionType, action.PolicyTypeFqn));

                if (!action.RequireAllPermissions.IsDefaultOrEmpty)
                    permissionEntries.Add((action.ActionType, action.RequireAllPermissions.AsImmutableArray(), true));
                else if (!action.RequireAnyPermissions.IsDefaultOrEmpty)
                    permissionEntries.Add((action.ActionType, action.RequireAnyPermissions.AsImmutableArray(), false));
            }
        }

        if (!_model.DiscoveredMutations.IsDefaultOrEmpty)
        {
            foreach (var mutation in _model.DiscoveredMutations)
            {
                if (mutation.PolicyTypeFqn is not null)
                    policyEntries.Add((mutation.MutationType, mutation.PolicyTypeFqn));

                if (!mutation.RequireAllPermissions.IsDefaultOrEmpty)
                    permissionEntries.Add((mutation.MutationType, mutation.RequireAllPermissions.AsImmutableArray(), true));
                else if (!mutation.RequireAnyPermissions.IsDefaultOrEmpty)
                    permissionEntries.Add((mutation.MutationType, mutation.RequireAnyPermissions.AsImmutableArray(), false));
            }
        }

        // Both registries are emitted UNCONDITIONALLY, empty ones included. The fallbacks they
        // displace are not empty registries that answer "nothing required" — UnavailablePolicyRegistry
        // and UnavailablePermissionRequirementRegistry both THROW, on purpose: a missing registry must
        // not be indistinguishable from an assembly that declares nothing, or a silenced generator
        // would let every [RequirePermission] through. That hardening assumes the generator always
        // emits something. Emitting only when there is something to put inside broke the assumption,
        // and any application with actions but no permissions failed with HTTP 500 on its first call.
        Comment("Policy registry — compile-time mapping of action types to policies (empty when none declared)");
        AppendLine("services.AddSingleton<global::Pragmatic.Actions.Pipeline.IPolicyRegistry>(new HostPolicyRegistry());");
        AppendLine();

        Comment("Permission requirement registry — compile-time mapping of action types to required permissions (empty when none declared)");
        AppendLine("services.AddSingleton<global::Pragmatic.Actions.Pipeline.IPermissionRequirementRegistry>(new HostPermissionRequirementRegistry());");
        AppendLine();

        // Store for later rendering of the nested classes
        _policyEntries = policyEntries;
        _permissionEntries = permissionEntries;
    }

    private List<(string ActionType, string PolicyType)>? _policyEntries;
    private List<(string ActionType, System.Collections.Immutable.ImmutableArray<string> Permissions, bool RequireAll)>? _permissionEntries;

    /// <summary>
    ///     Renders nested private classes for policy and permission registries inside PragmaticHost.
    /// </summary>
    private void RenderNestedRegistryClasses()
    {
        // Rendered whenever the registration above was emitted — that is, whenever there are actions.
        // A null list means RenderPolicyAndPermissionRegistries never ran (no actions at all), and
        // then nothing references these classes either.
        if (_policyEntries is not null)
        {
            AppendLine();
            RenderHostPolicyRegistry();
        }

        if (_permissionEntries is not null)
        {
            AppendLine();
            RenderHostPermissionRequirementRegistry();
        }
    }

    private void RenderHostPolicyRegistry()
    {
        AppendLine("private sealed class HostPolicyRegistry : global::Pragmatic.Actions.Pipeline.IPolicyRegistry");
        AppendLine("{");
        IncreaseIndent();

        AppendLine("private static readonly global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Func<global::Pragmatic.Authorization.Policy.ResourcePolicy>> Factories = new()");
        AppendLine("{");
        IncreaseIndent();
        foreach (var (actionType, policyType) in _policyEntries!)
        {
            // policyType already has global:: prefix from SymbolDisplayFormat.FullyQualifiedFormat
            var policyRef = policyType.StartsWith("global::") ? policyType : $"global::{policyType}";
            AppendLine($"[typeof(global::{actionType})] = static () => new {policyRef}(),");
        }
        DecreaseIndent();
        AppendLine("};");
        AppendLine();
        AppendLine("public global::Pragmatic.Authorization.Policy.ResourcePolicy? GetPolicy(global::System.Type actionType)");
        AppendLine("    => Factories.TryGetValue(actionType, out var factory) ? factory() : null;");

        DecreaseIndent();
        AppendLine("}");
    }

    private void RenderHostPermissionRequirementRegistry()
    {
        AppendLine("private sealed class HostPermissionRequirementRegistry : global::Pragmatic.Actions.Pipeline.IPermissionRequirementRegistry");
        AppendLine("{");
        IncreaseIndent();

        AppendLine("public global::Pragmatic.Actions.Pipeline.PermissionRequirementEntry? GetRequirement(global::System.Type actionType)");
        AppendLine("{");
        IncreaseIndent();

        foreach (var (actionType, permissions, requireAll) in _permissionEntries!)
        {
            var permsArray = string.Join(", ", permissions.Select(p => $"\"{p.Replace("\"", "\\\"")}\""));
            var requireAllStr = requireAll ? "true" : "false";
            AppendLine($"if (actionType == typeof(global::{actionType}))");
            IncreaseIndent();
            AppendLine($"return new global::Pragmatic.Actions.Pipeline.PermissionRequirementEntry(new[] {{ {permsArray} }}, {requireAllStr});");
            DecreaseIndent();
        }

        AppendLine("return null;");
        DecreaseIndent();
        AppendLine("}");

        DecreaseIndent();
        AppendLine("}");
    }
}
