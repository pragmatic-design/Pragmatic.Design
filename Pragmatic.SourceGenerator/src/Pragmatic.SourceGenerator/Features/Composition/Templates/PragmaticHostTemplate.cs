// Pragmatic.SourceGenerator - Composition - Pragmatic Host Template (Core)

using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates PragmaticHost.g.cs with categorized registration methods.
///     Core partial: constructor, fields, properties, RenderOutput, RenderFile, RenderHostClassBody, and shared helpers.
/// </summary>
internal sealed partial class PragmaticHostTemplate : CSharpTemplate
{
    private readonly ImmutableArray<StartupModel> _localStartups;
    private readonly HostAggregationModel _model;

    public PragmaticHostTemplate(HostAggregationModel model, ImmutableArray<StartupModel> localStartups)
    {
        _model = model;
        _localStartups = localStartups;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    /// <remarks>
    ///     Reads <see cref="HostAggregationModel.AllDomainModules"/>: a boundary the host declares in its
    ///     own project is one of its domain modules just as much as one it references.
    /// </remarks>
    private bool HasDomainModules => _model.AllDomainModules.Length > 0;
    private bool HasDatabaseRegistrations => _model.HasDatabaseRegistrations;

    private bool HasDiscoveredActions =>
        !_model.DiscoveredActions.IsDefaultOrEmpty || !_model.DiscoveredMutations.IsDefaultOrEmpty;

    private bool HasDiscoveredRepositories =>
        !_model.DiscoveredRepositories.IsDefaultOrEmpty;

    private bool HasDiscoveredServices =>
        !_model.DiscoveredServices.IsDefaultOrEmpty || !_model.DiscoveredDecorators.IsDefaultOrEmpty;

    private bool HasLocalServices =>
        !_model.LocalServices.IsDefaultOrEmpty || !_model.LocalDecorators.IsDefaultOrEmpty;

    // MetadataCategory.Validation = 4
    private bool HasValidationMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Validation && !string.IsNullOrEmpty(e.RegistrationMethod)));

    // MetadataCategory.EventHandlers = 11
    private bool HasEventHandlerMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.EventHandlers));

    // MetadataCategory 21 = TemporalBehaviors (cast in the emitting template; runtime enum stops at 17)
    private bool HasTemporalBehaviorsMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.TemporalBehaviors && !string.IsNullOrEmpty(e.RegistrationMethod)));

    // MetadataCategory.MessageHandlers = 15
    private bool HasMessageHandlerMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.MessageHandlers));

    private bool HasNeedsSteps => !_model.AggregatedNeedsSteps.IsDefaultOrEmpty;

    private bool HasPipelineSteps => _localStartups.Length > 0 || HasNeedsSteps;

    private bool HasEndpoints =>
        _model.DiscoveredEndpoints is { IsDefaultOrEmpty: false, Length: > 0 } ||
        HasExposedEndpoints;

    private bool HasExposedEndpoints =>
        _model.ExposedEndpoints is { IsDefaultOrEmpty: false, Length: > 0 };

    private bool HasRemoteBoundaries => _model.HasRemoteBoundaries;

    /// <summary>
    ///     Assemblies this host reaches over HTTP instead of hosting, from <c>[RemoteBoundary&lt;T&gt;]</c>.
    /// </summary>
    private HashSet<string> RemoteAssemblyNames
        => _model.HasRemoteBoundaries
            ? new HashSet<string>(
                _model.RemoteBoundaries
                    .Where(rb => rb.AssemblyName is not null)
                    .Select(rb => rb.AssemblyName!),
                StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    ///     True when this host has actions that are NOT from remote boundaries
    ///     (and therefore can serve as a remote invocation target).
    /// </summary>
    private bool HasLocalActions
    {
        get
        {
            if (_model.DiscoveredActions.IsDefaultOrEmpty)
                return false;

            if (!_model.HasRemoteBoundaries)
                return true;

            var remoteAssemblies = new HashSet<string>(
                _model.RemoteBoundaries
                    .Where(rb => rb.AssemblyName is not null)
                    .Select(rb => rb.AssemblyName!),
                StringComparer.Ordinal);

            return _model.DiscoveredActions.Any(a => !remoteAssemblies.Contains(a.SourceAssembly));
        }
    }

    private bool HasInfraModules
    {
        get
        {
            var f = _model.DetectedFeatures;
            return f.HasResilience || f.HasCaching || f.HasIdentityAspNetCore ||
                   f.HasMultiTenancy || f.HasFeatureFlags || f.HasDiscovery ||
                   f.HasTemporal || f.HasI18n || f.HasMessaging || f.HasJobs ||
                   f.HasNotifications;
        }
    }

    // MetadataCategory.Jobs = 16
    private bool HasJobMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Jobs));

    // MetadataCategory.Sagas = 17
    private bool HasSagaMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Sagas && !string.IsNullOrEmpty(e.RegistrationMethod)));

    // JsonContexts = 20 (generated JsonSerializerContext registrations)
    private bool HasJsonContextMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.JsonContexts && !string.IsNullOrEmpty(e.RegistrationMethod)));

    /// <summary>
    ///     Resilience = 28 — somebody in this application declared a policy.
    /// </summary>
    /// <remarks>
    ///     ⚠️ No <c>RegistrationMethod</c> check, unlike the categories around it: resilience has no
    ///     generated <c>Add*</c> to call, the host calls the package's own extension. What the entry
    ///     carries is the answer to «did anybody ask for this», and it is emitted only when somebody
    ///     did — an empty document would put the category on the compilation and put the question back
    ///     where it started.
    /// </remarks>
    private bool HasResilienceMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Resilience));

    /// <summary>
    ///     The host registers resilience: the package can be named and somebody declared a policy. One
    ///     condition for the registration and for the startup check of undefined policy names, so the
    ///     check never names a type the registration did not bring.
    /// </summary>
    private bool WiresResilience => _model.DetectedFeatures.HasResilience && HasResilienceMetadata;

    /// <summary>Caching = 13 — somebody declared a <c>[Cacheable]</c> or an invalidation.</summary>
    private bool HasCachingMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Caching));

    /// <summary>
    ///     FeatureFlags = 29 — somebody declared an <c>IFeatureFlag</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ No <c>RegistrationMethod</c> check, like resilience and for the same reason: there is no
    ///     generated <c>Add*</c> to call, the host calls the package's own extension. What the entry
    ///     carries is the answer to «did anybody ask for this».
    /// </remarks>
    private bool HasFeatureFlagMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.FeatureFlags));

    /// <summary>OutputCache = 33 — somebody declared a response the server may keep for any caller.</summary>
    private bool HasOutputCacheMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.OutputCache));

    /// <summary>Translations = 9 — somebody handed the generator a translation file.</summary>
    private bool HasTranslationsMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Translations));

    /// <summary>
    ///     I18nWireTypes = 31 — somebody puts a <c>Money</c> or another internationalization value type
    ///     on the wire.
    /// </summary>
    /// <remarks>
    ///     Its own declaration, because an application can have an amount on the wire and no translation
    ///     file at all: it then got no converters, and every request carrying one was a 400 before any
    ///     rule ran — on an application that started, since with no i18n registered it never reaches the
    ///     configuration check that would have refused.
    /// </remarks>
    private bool HasI18nWireTypeMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.I18nWireTypes));

    // Redaction = 25 (generated IRedactionMap registrations)
    private bool HasRedactionMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Redaction && !string.IsNullOrEmpty(e.RegistrationMethod)));

    // RollUpRules = 26 (generated RollUpRule registrations for [RollUp] aggregates)
    private bool HasRollUpRuleMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.RollUpRules && !string.IsNullOrEmpty(e.RegistrationMethod)));

    // Authorization = 22 (generated PermissionRegistry/RoleRegistry → DefaultPermissionCatalog)
    private bool HasAuthorizationCatalogMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Authorization && !string.IsNullOrEmpty(e.RegistrationMethod)));

    // PersonalData = 23 (generated IPersonalDataSource / IErasureStep / IProcessingActivitySource).
    // Without this call the data-subject services resolve empty enumerables and report success: an
    // access request that returns nothing and an erasure that erases nothing both look like they worked.
    private bool HasPrivacyAdapterMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.PersonalData && !string.IsNullOrEmpty(e.RegistrationMethod)));

    // Configuration = 14 (the declared [Configuration] sections). Without this call IConfigurationCatalog
    // resolves to nothing and ConfigurationPreflight passes any configuration at all, because it has
    // nothing to check it against — a check that cannot fail.
    private bool HasConfigurationCatalogMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.Configuration && !string.IsNullOrEmpty(e.RegistrationMethod)));

    // ReadContracts = 27 (the I{Module}Reads a [Published] query produces). Without this call the
    // contract's interface, implementation and Add{Module}Reads were all generated and nothing bound
    // them: an action injecting it failed at resolution, on the first request rather than at startup.
    private bool HasReadContractMetadata =>
        _model.Assemblies.Any(a => a.Entries.Any(e => e.Category == MetadataCategoryIds.ReadContracts && !string.IsNullOrEmpty(e.RegistrationMethod)));

    public override Artifact RenderOutput()
    {
        return new Artifact("Host.Services.g.cs", ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.Extensions.Configuration");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.Hosting");
        AddUsing("Pragmatic.Composition.Abstractions");

        // Emit host topology metadata attribute (for cross-deployment analysis tooling)
        if (!_model.HostIncludes.IsDefaultOrEmpty || HasDomainModules)
        {
            AddUsing("Pragmatic.Composition.Attributes");
            AddUsing("Pragmatic.Composition.Metadata");
            AppendLine();
            RenderTopologyMetadataAttribute();
        }

        // Generate PragmaticHost class in project namespace
        AppendNamespace(_model.RootNamespace);
        AppendLine();

        Class("PragmaticHost", RenderHostClassBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderHostClassBody()
    {
        // 1. RegisterAllDomainActions
        RenderRegisterAllDomainActionsMethod();
        AppendLine();

        // 2. RegisterAllRepositories (skip if no repositories discovered)
        if (HasDiscoveredRepositories)
        {
            RenderRegisterAllRepositoriesMethod();
            AppendLine();
        }

        // 3. RegisterAllPipelineSteps (skip if no steps discovered)
        if (HasPipelineSteps)
        {
            RenderRegisterAllPipelineStepsMethod();
            AppendLine();
        }

        // 4. RegisterAllEndpoints
        RenderRegisterAllEndpointsMethod();
        AppendLine();

        // 5. RegisterAllPragmaticServices (main aggregator)
        RenderRegisterAllPragmaticServicesMethod();
        AppendLine();

        // 6. CallConfigureServices
        RenderCallConfigureServicesMethod();

        // 6-bis. ApplyDecorators — last, so a decorator can wrap anything the steps registered
        RenderApplyDecoratorsMethod();
        AppendLine();

        // 7. ConfigurePipeline
        RenderConfigurePipelineMethod();
        AppendLine();

        // 8. MapAllEndpoints
        RenderMapAllEndpointsMethod();

        // 9. RegisterAllDatabases (only when [Include<T,D,C>] entries exist)
        if (HasDatabaseRegistrations)
        {
            AppendLine();
            RenderRegisterAllDatabasesMethod();
        }

        // 10. ValidateConfiguration (only when modules declare [RequiresConfig])
        if (_model.HasRequiredConfigValidation)
        {
            AppendLine();
            RenderValidateConfigurationMethod();
        }

        // 11. Nested registry classes for policy and permission enforcement
        RenderNestedRegistryClasses();
    }

    // =========================================================================
    // Shared Helpers
    // =========================================================================

    private static string GetSimpleName(string fullTypeName)
    {
        var clean = fullTypeName.Replace("global::", string.Empty);
        var lastDot = clean.LastIndexOf('.');
        return lastDot >= 0 ? clean.Substring(lastDot + 1) : clean;
    }

    /// <summary>
    ///     Extracts the namespace from a fully-qualified boundary type name.
    ///     E.g. <c>global::Showcase.Billing.BillingBoundary</c> -> <c>Showcase.Billing</c>.
    /// </summary>
    private static string ExtractNamespaceFromBoundaryType(string boundaryTypeName)
    {
        var clean = boundaryTypeName.Replace("global::", string.Empty);
        var lastDot = clean.LastIndexOf('.');
        return lastDot > 0 ? clean.Substring(0, lastDot) : clean;
    }

    /// <summary>
    ///     Derives the module simple name from its fully qualified type name by stripping the "Module" suffix.
    ///     E.g. <c>global::Showcase.Booking.BookingModule</c> -> <c>Booking</c>.
    /// </summary>
    private static string GetModuleSimpleName(string fullTypeName)
    {
        var simple = GetSimpleName(fullTypeName);
        return simple.EndsWith("Module", StringComparison.Ordinal)
            ? simple.Substring(0, simple.Length - 6)
            : simple;
    }

    /// <summary>Converts PascalCase to camelCase.</summary>
    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    private static string EscapeLiteral(string value)
    {
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    // =========================================================================
    // Topology Metadata
    // =========================================================================

    /// <summary>
    ///     Emits [assembly: PragmaticMetadata(MetadataCategory.HostTopology, ...)] with a compact JSON
    ///     describing this host's module -> database -> DbContext wiring.
    ///     Enables cross-deployment topology validation by external tooling.
    /// </summary>
    private void RenderTopologyMetadataAttribute()
    {
        var json = BuildTopologyJson();
        var escaped = json.Replace("\\", "\\\\").Replace("\"", "\\\"");
        AppendLine($"[assembly: PragmaticMetadata(MetadataCategory.HostTopology, \"{MetadataSchemaVersions.HostTopology}\", \"{escaped}\")]");
    }

    /// <summary>
    ///     The compact HostTopology JSON for this host, or null when the host declares no includes and no
    ///     domain modules (nothing to describe). Used to emit both the metadata attribute and the
    ///     AssemblyMetadataRegistry provider (zero-reflection runtime read via HostTopologyInfo.FromRegistry).
    /// </summary>
    internal string? BuildTopologyJsonOrNull()
        => !_model.HostIncludes.IsDefaultOrEmpty || HasDomainModules
            ? BuildTopologyJson()
            : null;

    /// <summary>
    ///     This host's logical name — the one its topology carries and the one its identity reports.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ One expression for both, on purpose. The topology JSON and the registration of
    ///         <c>LocalHostIdentity</c> are the two places the name is written, and a host whose identity
    ///         disagreed with its own topology would be worse than one filed under a neighbour's name:
    ///         the answer would be wrong and self-consistent nowhere.
    ///     </para>
    ///     <para>
    ///         ⚠️ The <b>whole</b> root namespace, not its last segment. <c>GetSimpleName(RootNamespace)</c>
    ///         answers <c>"Host"</c> for the conventional <c>App.Service.Host</c> layout — measured on
    ///         Casework, where <b>both</b> services' topologies would say <c>"Host"</c>. A test that
    ///         passes while the two hosts stay indistinguishable is the shape this whole family of
    ///         defects is made of.
    ///     </para>
    /// </remarks>
    internal string HostName =>
        string.IsNullOrWhiteSpace(_model.RootNamespace) ? "PragmaticHost" : _model.RootNamespace;

    private string BuildTopologyJson()
    {
        var sb = new System.Text.StringBuilder();
        var hostName = JsonEscape(HostName);
        sb.Append($"{{\"host\":\"{hostName}\",\"includes\":[");

        var first = true;
        foreach (var include in _model.HostIncludes)
        {
            if (!first)
                sb.Append(',');
            first = false;

            sb.Append('{');
            sb.Append($"\"module\":\"{JsonEscape(GetSimpleName(include.ModuleTypeName))}\"");
            if (include.DatabaseTypeName is not null)
                sb.Append($",\"database\":\"{JsonEscape(GetSimpleName(include.DatabaseTypeName))}\"");
            if (include.DatabaseProvider is not null)
                sb.Append($",\"provider\":\"{JsonEscape(include.DatabaseProvider)}\"");
            if (include.DatabaseConfigKey is not null)
                sb.Append($",\"configKey\":\"{JsonEscape(include.DatabaseConfigKey)}\"");
            if (include.DbContextClassName is not null)
                sb.Append($",\"dbContext\":\"{JsonEscape(include.DbContextClassName)}\"");
            sb.Append('}');
        }

        sb.Append("],\"boundaries\":[");

        var firstBoundary = true;
        foreach (var module in _model.AllDomainModules)
        {
            if (module.ReadAccessTypes.IsDefaultOrEmpty)
                continue;

            if (!firstBoundary)
                sb.Append(',');
            firstBoundary = false;

            sb.Append('{');
            sb.Append($"\"name\":\"{JsonEscape(module.Name)}\"");
            sb.Append(",\"readAccess\":[");
            sb.Append(string.Join(",", module.ReadAccessTypes.Select(t => $"\"{JsonEscape(GetSimpleName(t))}\"")));
            sb.Append(']');
            sb.Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
