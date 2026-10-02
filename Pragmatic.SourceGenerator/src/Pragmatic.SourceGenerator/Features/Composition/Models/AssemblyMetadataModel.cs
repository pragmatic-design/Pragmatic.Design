// Pragmatic.SourceGenerator - Composition - Assembly Metadata Model

using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Immutable model for metadata discovered from a referenced assembly.
/// </summary>
internal sealed record AssemblyMetadataModel
{
    /// <summary>Gets the assembly name.</summary>
    public required string AssemblyName { get; init; }

    /// <summary>Gets the metadata entries from this assembly.</summary>
    public required EquatableArray<MetadataEntry> Entries { get; init; }
}

/// <summary>
///     A single metadata entry from [PragmaticMetadata] attribute.
/// </summary>
internal sealed record MetadataEntry
{
    /// <summary>Gets the category (DI, Mapping, Actions, etc.).</summary>
    public required string Category { get; init; }

    /// <summary>Gets the schema version.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Gets the registration method to call.</summary>
    public required string RegistrationMethod { get; init; }

    /// <summary>
    ///     A second registration method the same document may declare, empty when it declares none.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>registrationMethod</c> is one slot, and Actions needs two: an assembly can generate
    ///     invoker registrations for its actions, for its mutations, or for both. Same shape as
    ///     Persistence's <c>lookupRegistrationMethod</c> beside its query filters — both are read the
    ///     same way, and a category that needs only one leaves this empty.
    /// </remarks>
    public string SecondaryRegistrationMethod { get; init; } = string.Empty;

    /// <summary>Gets the raw JSON data.</summary>
    public required string JsonData { get; init; }
}

/// <summary>
///     Aggregated model for HOST mode generation.
/// </summary>
internal sealed record HostAggregationModel
{
    /// <summary>Gets all assembly metadata models.</summary>
    public required EquatableArray<AssemblyMetadataModel> Assemblies { get; init; }

    /// <summary>Gets the root namespace for generated code.</summary>
    public required string RootNamespace { get; init; }

    /// <summary>Gets whether to generate topology report (Debug only).</summary>
    public required bool GenerateTopologyReport { get; init; }

    /// <summary>Gets validation errors found during aggregation.</summary>
    public required EquatableArray<ValidationError> ValidationErrors { get; init; }

    /// <summary>Gets discovered modules from metadata.</summary>
    public EquatableArray<DiscoveredModuleInfo> DiscoveredModules { get; init; } =
        EquatableArray<DiscoveredModuleInfo>.Empty;

    /// <summary>Gets local modules defined in this assembly.</summary>
    public EquatableArray<ModuleModel> LocalModules { get; init; } = EquatableArray<ModuleModel>.Empty;

    /// <summary>
    ///     The assemblies this host composes, after the <c>[Include&lt;T&gt;]</c> filter — empty when
    ///     the host declares no topology and therefore composes everything it can see.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The <c>Discovered*</c> lists arrive filtered already; this is for the readers that work
    ///     from <see cref="AllDomainModules" /> instead, which is every boundary in the compilation.
    ///     Without it the boundary-registration loop called <c>Add{Module}Boundary</c> for a module the
    ///     host does not host, because that module imports a package — and a boundary extension
    ///     registers its package's invokers. The standalone Billing host then failed container
    ///     validation before any request: "Unable to resolve service for type 'IRolePermissionStore'".
    /// </remarks>
    public EquatableArray<string> IncludedAssemblyNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Gets discovered Domain modules from [PragmaticModuleMetadata] attributes.</summary>
    public EquatableArray<DiscoveredModuleInfo> DomainModules { get; init; } =
        EquatableArray<DiscoveredModuleInfo>.Empty;

    /// <summary>
    ///     Gets the Domain modules the host declares in its own project, contributed directly by the
    ///     feature that writes their <c>[PragmaticModuleMetadata]</c>.
    /// </summary>
    /// <remarks>
    ///     Deliberately not merged into <see cref="DomainModules"/>. That property means "modules this
    ///     host references", and three readers depend on exactly that meaning: the
    ///     <c>[Include&lt;T&gt;]</c>-to-assembly map and the <c>{Module}DbContext</c> lookup depend on
    ///     exactly that meaning. A boundary the host declares itself is not something it can choose to
    ///     include. Read <see cref="AllDomainModules"/> where the question is "every boundary this host
    ///     composes".
        /// </remarks>
    public EquatableArray<DiscoveredModuleInfo> LocalDomainModules { get; init; } =
        EquatableArray<DiscoveredModuleInfo>.Empty;

    /// <summary>
    ///     Every boundary this host composes: the ones it references and the ones it declares. Ordered by
    ///     name so the generated output does not depend on which side a module came from.
    /// </summary>
    /// <remarks>
    ///     Not a collection expression: the generator compiles as netstandard2.0, whose
    ///     <c>ImmutableArray&lt;T&gt;</c> predates <c>[CollectionBuilder]</c> (CS9210).
    /// </remarks>
    public ImmutableArray<DiscoveredModuleInfo> AllDomainModules =>
        DomainModules.AsImmutableArray()
            .AddRange(LocalDomainModules.AsImmutableArray())
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToImmutableArray();

    /// <summary>Gets discovered endpoint types from enriched Endpoints metadata.</summary>
    public EquatableArray<DiscoveredEndpointRouteInfo> DiscoveredEndpoints { get; init; } =
        EquatableArray<DiscoveredEndpointRouteInfo>.Empty;

    /// <summary>Gets discovered endpoint groups from enriched Endpoints metadata.</summary>
    public EquatableArray<DiscoveredEndpointGroupInfo> DiscoveredEndpointGroups { get; init; } =
        EquatableArray<DiscoveredEndpointGroupInfo>.Empty;

    /// <summary>Gets whether any discovered endpoint assembly uses ASP.NET API versioning.</summary>
    public bool HasAspVersioning { get; init; }

    /// <summary>
    ///     Gets the aggregated [Include&lt;T&gt;] host wiring declarations from the local [Module] class.
    ///     Populated by HostModeGenerator from localModules.
    /// </summary>
    public EquatableArray<HostIncludeModel> HostIncludes { get; init; } =
        EquatableArray<HostIncludeModel>.Empty;

    /// <summary>
    ///     True when there are any [Include&lt;T, D&gt;] or [Include&lt;T, D, C&gt;] entries with a database assignment,
    ///     meaning database registration code should be generated.
    /// </summary>
    public bool HasDatabaseRegistrations =>
        !HostIncludes.IsDefaultOrEmpty && HostIncludes.Any(i => i.HasDatabase);

    /// <summary>
    ///     The <c>Add{Boundary}DbContext</c> registrations and the databases the persistence generator
    ///     writes into this host. Nothing else may be called: a module with no entities yet gets neither.
    /// </summary>
    public required Persistence.Models.PersistedStoresModel PersistedStores { get; init; }

    /// <summary>Gets discovered service registrations from enriched DI metadata in referenced assemblies.</summary>
    public EquatableArray<DiscoveredServiceInfo> DiscoveredServices { get; init; } =
        EquatableArray<DiscoveredServiceInfo>.Empty;

    /// <summary>Gets discovered decorator registrations from enriched DI metadata in referenced assemblies.</summary>
    public EquatableArray<DiscoveredDecoratorInfo> DiscoveredDecorators { get; init; } =
        EquatableArray<DiscoveredDecoratorInfo>.Empty;

    /// <summary>Gets local services defined in the host project (for direct registration).</summary>
    public EquatableArray<ServiceModel> LocalServices { get; init; } =
        EquatableArray<ServiceModel>.Empty;

    /// <summary>Gets local decorators defined in the host project (for direct registration).</summary>
    public EquatableArray<DecoratorModel> LocalDecorators { get; init; } =
        EquatableArray<DecoratorModel>.Empty;

    /// <summary>
    ///     Whether anything at all is decorated, from either source. The entry point needs the answer
    ///     to decide whether to call <c>ApplyDecorators</c>, which is only emitted when there is one.
    /// </summary>
    public bool HasDecorators =>
        !DiscoveredDecorators.IsDefaultOrEmpty || !LocalDecorators.IsDefaultOrEmpty;

    /// <summary>Gets discovered action registrations from enriched Actions metadata in referenced assemblies.</summary>
    public EquatableArray<DiscoveredActionInfo> DiscoveredActions { get; init; } =
        EquatableArray<DiscoveredActionInfo>.Empty;

    /// <summary>Gets discovered mutation registrations from enriched Actions metadata in referenced assemblies.</summary>
    public EquatableArray<DiscoveredMutationInfo> DiscoveredMutations { get; init; } =
        EquatableArray<DiscoveredMutationInfo>.Empty;

    /// <summary>Gets discovered repository registrations from enriched Persistence metadata in referenced assemblies.</summary>
    public EquatableArray<DiscoveredRepositoryInfo> DiscoveredRepositories { get; init; } =
        EquatableArray<DiscoveredRepositoryInfo>.Empty;

    /// <summary>
    ///     The generated <c>Add{Prefix}LookupCaches()</c> of every assembly that declares a
    ///     <c>[Lookup]</c>, so the host calls it.
    /// </summary>
    /// <remarks>
    ///     Kept apart from the query-filter <c>RegistrationMethod</c> on the entry: the Persistence
    ///     document has one slot with that name and the filters hold it.
    /// </remarks>
    public EquatableArray<string> DiscoveredLookupRegistrations { get; init; } =
        EquatableArray<string>.Empty;

    /// <summary>Gets the aggregated required configuration sections from all module setups.</summary>
    public EquatableArray<RequiredConfigSectionInfo> RequiredConfigSections { get; init; } =
        EquatableArray<RequiredConfigSectionInfo>.Empty;

    /// <summary>
    ///     Deduplicated fully qualified step type names from [NeedsStep&lt;T&gt;] across all modules.
    /// </summary>
    public EquatableArray<string> AggregatedNeedsSteps { get; init; } = EquatableArray<string>.Empty;

    /// <summary>True when at least one module declares required configuration sections via [RequiresConfig].</summary>
    public bool HasRequiredConfigValidation =>
        !RequiredConfigSections.IsDefaultOrEmpty;

    /// <summary>True when Pragmatic.Persistence.Serialization.EntityJsonModifier is available in the compilation.</summary>
    public bool HasPersistenceSerialization { get; init; }

    /// <summary>
    ///     True when this host has a generated compile-time OpenAPI document to register in its own
    ///     container, so the document is per host and not per process.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It is the same condition as "the host can serve the document": when
    ///     <c>Pragmatic.Endpoints.OpenApi</c> is referenced, <c>GenerateOpenApiDocument</c> always emits
    ///     one (with no operations before the host's first endpoint), so registry-available and
    ///     document-generated coincide exactly. Registering it makes the document a fact about
    ///     <b>this</b> host. A process-wide static written per host by a <c>[ModuleInitializer]</c> would
    ///     let the host loaded second answer for both when two hosts share one process.
    /// </remarks>
    public bool HasOpenApiDocument { get; init; }

    /// <summary>
    ///     True when this host has a generated aggregated manifest to register in its own container,
    ///     so the manifest is per host and not per process.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The same shape as <see cref="HasOpenApiDocument" />, one registry along, and one degree
    ///     subtler. <c>ManifestRegistry</c> <b>accumulates</b> rather than overwrites, so two hosts in
    ///     one process share both manifests instead of losing one: the enrichment itself stays
    ///     route-scoped, but <c>requiresAuthentication</c> is computed over the whole lookup, so an
    ///     anonymous host beside an authenticated one publishes security schemes for operations it does
    ///     not have.
    /// </remarks>
    public bool HasAggregatedManifest { get; init; }

    /// <summary>Detected runtime features for auto-registration of infrastructure modules.</summary>
    public Core.DetectedFeatures DetectedFeatures { get; init; } = Core.DetectedFeatures.None;

    /// <summary>
    ///     Cache category FQNs discovered from [Cacheable(Category=...)] and [InvalidatesCache(Category=...)]
    ///     across all referenced assemblies. Used to auto-register keyed ICacheStack instances.
    /// </summary>
    public EquatableArray<string> DiscoveredCacheCategories { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The languages the referenced modules' translations are written in: the host registers them as
    ///     the culture configuration below whatever the application configures.
    /// </summary>
    public DeclaredLanguagesModel DeclaredLanguages { get; init; } = DeclaredLanguagesModel.None;

    /// <summary>
    ///     The generated <c>ILocalizationProvider</c>s over the translations the referenced modules embed
    ///     (<c>[TranslationKeys(EmbedTranslations = true)]</c>): registered by the host, so a lookup by key
    ///     finds what a module compiled in without its files being copied beside the host.
    /// </summary>
    public EquatableArray<string> TranslationProviders { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The anchor types of the assemblies that embed document and mail templates
    ///     (<c>[assembly: PdxTemplates&lt;TAnchor&gt;]</c>): the host registers each as a template source.
    /// </summary>
    public EquatableArray<string> PdxTemplateAnchors { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Assembly names that are packages (via [UsePackage]). Used to skip duplicate invoker registration
    ///     since package invokers are already registered by the boundary extension.
    /// </summary>
    public EquatableArray<string> PackageAssemblyNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Exposed endpoints from [ExposeEndpoint&lt;T&gt;] declarations on local and discovered modules.
    ///     The host generates endpoint handler classes and maps them in MapAllEndpoints.
    /// </summary>
    public EquatableArray<ExposedEndpointModel> ExposedEndpoints { get; init; } = EquatableArray<ExposedEndpointModel>.Empty;

    /// <summary>
    ///     Remote boundary declarations from [RemoteBoundary&lt;T&gt;] on local modules.
    ///     Actions from these modules are invoked via HTTP instead of in-process.
    /// </summary>
    public EquatableArray<RemoteBoundaryModel> RemoteBoundaries { get; init; } = EquatableArray<RemoteBoundaryModel>.Empty;

    /// <summary>Whether this host has any remote boundary declarations.</summary>
    public bool HasRemoteBoundaries => RemoteBoundaries is { IsDefaultOrEmpty: false, Length: > 0 };
}

/// <summary>
///     Validation error found during HOST aggregation.
/// </summary>
/// <remarks>
///     Carries the message <b>arguments</b> rather than a finished sentence. A finished sentence
///     passed as <c>{0}</c> of a descriptor whose format wants two arguments shows the user
///     <c>"Metadata schema version {0} in {1} is newer than supported"</c>, with the braces intact and
///     neither the version nor the assembly named. The descriptor is the one place that words it.
/// </remarks>
internal sealed record ValidationError
{
    /// <summary>Gets the diagnostic ID (e.g., PRAG1602).</summary>
    public required string DiagnosticId { get; init; }

    /// <summary>Gets the arguments for the descriptor's message format, in order.</summary>
    public required EquatableArray<string> MessageArgs { get; init; }

    /// <summary>Gets the source assembly name.</summary>
    public required string? AssemblyName { get; init; }
}

/// <summary>
///     A required configuration section declared via [RequiresConfig] on a module setup class.
/// </summary>
internal sealed record RequiredConfigSectionInfo
{
    /// <summary>Gets the configuration section path (e.g., "ConnectionStrings:Booking").</summary>
    public required string SectionPath { get; init; }

    /// <summary>Gets the fully qualified type name of the module setup that declared this requirement.</summary>
    public required string SourceModuleType { get; init; }
}
