using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed class ActionsMetadataTemplate : CSharpTemplate
{
    private readonly ImmutableArray<ActionModel> _actions;
    private readonly ImmutableArray<MutationModel> _mutations;
    private readonly bool _indent;

    public ActionsMetadataTemplate(
        ImmutableArray<ActionModel> actions,
        ImmutableArray<MutationModel> mutations,
        bool indent)
    {
        _actions = actions;
        _mutations = mutations;
        _indent = indent;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForMetadata("Actions"),
        ToSourceText());

    protected override bool Validate() => !_actions.IsEmpty || !_mutations.IsEmpty;

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Composition.Attributes");
        AddUsing("Pragmatic.Composition.Metadata");

        AppendLine();

        var json = BuildJson();

        AppendLine(
            $"[assembly: PragmaticMetadata(MetadataCategory.Actions, \"{MetadataSchemaVersions.Actions}\", \"\"\"");
        AppendLine(json);
        AppendLine("\"\"\")]");
    }

    /// <summary>
    ///     The metadata document this template writes. Public because a host that declares its own
    ///     actions has to hand the same document to Composition directly — the attribute above is
    ///     emitted into that same compilation and can never be read back off it.
    /// </summary>
    public string BuildJson()
    {
        var builder = new MetadataJsonBuilder(_indent);

        builder.StartObject();
        builder.Property("generator", "Pragmatic.Actions.SourceGenerator");

        // The two entry points this assembly generates for its own invokers, so the host calls them
        // instead of writing the same registrations a second time. A host that emitted one AddScoped
        // per operation would register the same pairs, and would have to **name** types from another
        // assembly — so a generated invoker could never be internal.
        // ⚠️ Two slots, because an assembly can have actions, mutations, or both, and one string cannot
        // hold two names. Same shape as Persistence's "lookupRegistrationMethod" beside its query filters.
        var actionsRegistration = ActionsRegistrationTemplate.RegistrationMethodFor(_actions);
        if (actionsRegistration is null)
            builder.PropertyNull("registrationMethod");
        else
            builder.Property("registrationMethod", actionsRegistration);

        var mutationsRegistration = MutationRegistrationTemplate.RegistrationMethodFor(_mutations);
        if (mutationsRegistration is null)
            builder.PropertyNull("mutationsRegistrationMethod");
        else
            builder.Property("mutationsRegistrationMethod", mutationsRegistration);

        builder.Property("data");
        builder.StartObject();
        builder.Property("actionsCount", _actions.Length);

        // Actions with invoker type info
        builder.Property("actions");
        builder.StartArray();

        foreach (var action in _actions.OrderBy(a => a.FullQualifiedName))
        {
            builder.StartObject();
            builder.Property("type", action.FullQualifiedName);
            builder.Property("isVoid", action.IsVoid);

            if (action is { IsVoid: false, ReturnTypeName: not null })
                builder.Property("returnType", action.ReturnTypeName);

            // Invoker type for direct DI registration by the host
            builder.Property("invokerType", BuildInvokerTypeName(action.FullQualifiedName));

            // The declared compensator travels with the action: the host builds its own registration
            // list from this metadata and never calls the module's Add*Actions extension, so a
            // registration emitted only there would be a producer with no caller.
            if (action.CompensatorTypeName is not null)
                builder.Property("compensatorType", action.CompensatorTypeName);

            // The host registers the CompositeInvoker and the concrete step invokers from
            // here. The two are separate facts: a composite made only of action steps has a
            // CompositeInvoker to register and no concrete invoker to go with it, so the host cannot
            // infer the first from the second being present.
            if (action.HasCompositeSteps)
                builder.Property("hasCompositeSteps", true);

            if (!action.CompositeStepInvokerTypes.IsDefaultOrEmpty)
            {
                builder.Property("compositeStepInvokers");
                builder.StartArray();
                foreach (var stepInvoker in action.CompositeStepInvokerTypes)
                    builder.Value(stepInvoker);
                builder.EndArray();
            }

            WriteBoundaryKeyedNeeds(builder, action.Dependencies);

            if (action.IsInternal)
                builder.Property("internal", true);

            if (action.IsSystem)
                builder.Property("system", true);

            if (action.HasDependencies)
                builder.Property("dependenciesCount", action.Dependencies.Length);

            // Policy and permission metadata for host-level registry generation
            if (action.HasPolicy)
                builder.Property("policyType", action.PolicyTypeFullName!);

            if (action.HasPermissionRequirement)
            {
                if (!action.RequireAllPermissions.IsDefaultOrEmpty)
                {
                    builder.Property("requireAllPermissions");
                    builder.StartArray();
                    foreach (var p in action.RequireAllPermissions)
                        builder.Value(p);
                    builder.EndArray();
                }

                if (!action.RequireAnyPermissions.IsDefaultOrEmpty)
                {
                    builder.Property("requireAnyPermissions");
                    builder.StartArray();
                    foreach (var p in action.RequireAnyPermissions)
                        builder.Value(p);
                    builder.EndArray();
                }
            }

            builder.EndObject();
        }

        builder.EndArray();

        // Mutations with invoker type info
        builder.Property("mutationsCount", _mutations.Length);
        builder.Property("mutations");
        builder.StartArray();

        foreach (var mutation in _mutations.OrderBy(m => m.FullQualifiedName))
        {
            builder.StartObject();
            builder.Property("type", mutation.FullQualifiedName);
            builder.Property("entityType", mutation.EntityFullTypeName);

            // What the mutation returns when it is not the entity: an importer generates the boundary
            // member for a package's mutation from this metadata alone, and has to return the same thing
            // the package's own member does.
            if (mutation.EffectiveReturnType != MutationReturnTypeValue.Entity)
                builder.Property("returnKind", mutation.EffectiveReturnType.ToString());

            // Mutation invoker type for direct DI registration by the host
            builder.Property("invokerType", BuildMutationInvokerTypeName(mutation.FullQualifiedName));

            if (mutation.CompensatorTypeName is not null)
                builder.Property("compensatorType", mutation.CompensatorTypeName);

                // ⚠️ The preset providers travel with the mutation for the same reason the compensator
                // does: the host registers services directly from this manifest rather than calling the
                // module's own registration method, so a type the module knows and the manifest does not
                // is a type the container never hears of. Measured — the provider was registered in
                // _Infra.Mutations.Registration.g.cs, which nothing calls, and every creation of the
                // entity answered 500.
                if (mutation.Presets is { } presets && presets.Providers.Length > 0)
                {
                    builder.Property("presetProviders");
                    builder.StartArray();
                    foreach (var provider in presets.Providers)
                        builder.Value(provider.ProviderTypeFqn);
                    builder.EndArray();
                }

            if (mutation.IsInternal)
                builder.Property("internal", true);

            // Serialize computed default generators for host-level DI registration
            if (mutation.HasComputedDefaults && mutation.ComputedDefaults?.Properties.Length > 0)
            {
                builder.Property("computedDefaults");
                builder.StartArray();
                foreach (var cd in mutation.ComputedDefaults.Properties)
                {
                    builder.StartObject();
                    builder.Property("entityType", cd.EntityTypeFqn);
                    builder.Property("valueType", cd.ValueTypeFqn);
                    builder.Property("generatorType", cd.GeneratorTypeFqn);
                    if (cd.RequiresScope)
                        builder.Property("requiresScope", "true");
                    builder.EndObject();
                }

                builder.EndArray();
            }

            // Policy and permission metadata for host-level registry generation
            if (mutation.HasPolicy)
                builder.Property("policyType", mutation.PolicyTypeFullName!);

            if (mutation.HasPermissionRequirement)
            {
                if (!mutation.RequireAllPermissions.IsDefaultOrEmpty)
                {
                    builder.Property("requireAllPermissions");
                    builder.StartArray();
                    foreach (var p in mutation.RequireAllPermissions)
                        builder.Value(p);
                    builder.EndArray();
                }

                if (!mutation.RequireAnyPermissions.IsDefaultOrEmpty)
                {
                    builder.Property("requireAnyPermissions");
                    builder.StartArray();
                    foreach (var p in mutation.RequireAnyPermissions)
                        builder.Value(p);
                    builder.EndArray();
                }
            }

            builder.EndObject();
        }

        builder.EndArray();

        builder.EndObject();
        builder.EndObject();

        return builder.ToString();
    }

    /// <summary>
    ///     Builds the fully qualified invoker type name for a DomainAction.
    ///     Convention: nested class Invoker inside the action type.
    /// </summary>
    private static string BuildInvokerTypeName(string actionFullQualifiedName)
        => $"global::{actionFullQualifiedName}.Invoker";

    /// <summary>
    ///     Builds the fully qualified mutation invoker type name.
    ///     Convention: nested class Invoker inside the mutation type.
    /// </summary>
    private static string BuildMutationInvokerTypeName(string mutationFullQualifiedName)
        => $"global::{mutationFullQualifiedName}.Invoker";

    /// <summary>
    ///     The boundary-keyed services this operation asks for <b>without</b> a key.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>DbContext</c> and <c>IUnitOfWork</c> are registered keyed by boundary. An assembly
    ///         that declares no <c>[Boundary]</c> — a package — has no key to give, so the generated
    ///         invoker asks for them unkeyed and its constructor is fixed here: the module that imports
    ///         the package cannot key it afterwards.
    ///     </para>
    ///     <para>
    ///         So the package <b>declares the need</b> and the importer answers it, which is the shape
    ///         every other cross-assembly fact in this generator has. Written only when there is one:
    ///         a property on every operation would make an importer supply a boundary for packages that
    ///         never touch persistence.
    ///     </para>
    /// </remarks>
    private static void WriteBoundaryKeyedNeeds(
        MetadataJsonBuilder builder,
        EquatableArray<Models.DependencyModel> dependencies)
    {
        if (dependencies.IsDefaultOrEmpty)
            return;

        var unkeyed = dependencies
            .Where(d => string.IsNullOrEmpty(d.KeyedServiceType)
                        && Transforms.BoundaryKeyedServices.IsKeyedByBoundary(d.TypeName))
            .Select(d => d.TypeName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (unkeyed.Count == 0)
            return;

        builder.Property("boundaryKeyedServices");
        builder.StartArray();
        foreach (var name in unkeyed)
            builder.Value(name);
        builder.EndArray();
    }
}
