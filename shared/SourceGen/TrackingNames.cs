// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Stable names attached to incremental pipeline steps via <c>WithTrackingName</c> so incrementality
///     tests can assert a step stays cached. Names are referenced from both the generator and the tests.
///     Convention: <c>{Feature}.{Provider}</c>. Only the significant nodes are tagged — the output of a
///     transform (the <c>Select</c>/<c>Where</c> that produces the model) and the <c>Collect()</c>
///     aggregates — not every intermediate node.
/// </summary>
internal static class TrackingNames
{
    // ── Persistence ──
    public const string PersistenceReferencedEntities = "Persistence.ReferencedEntities";
    public const string PersistenceCurrentEntities = "Persistence.CurrentEntities";
    public const string PersistenceAllEntities = "Persistence.AllEntities";

    // ── Persistence / DbContext ── (metadata reads that feed DbContext + schema generation)
    public const string PersistenceReadAccess = "Persistence.ReadAccess";
    public const string PersistenceDatabaseTopology = "Persistence.DatabaseTopology";
    public const string PersistenceTraitEntities = "Persistence.TraitEntities";
    public const string PersistenceDbContextInput = "Persistence.DbContextInput";

    // ── Actions ──
    public const string ActionsActions = "Actions.Actions";
    public const string ActionsAllActions = "Actions.AllActions";
    public const string ActionsMutations = "Actions.Mutations";
    public const string ActionsAllMutations = "Actions.AllMutations";
    public const string ActionsBoundaries = "Actions.Boundaries";

    /// <summary>The boundaries in play: those declared, or the one derived from the module.</summary>
    public const string ActionsEffectiveBoundaries = "Actions.EffectiveBoundaries";

    /// <summary>Which boundaries the assembly declares, and what each claims with [Owns&lt;T&gt;].</summary>
    public const string PersistenceBoundaryOwnership = "Persistence.BoundaryOwnership";

    // ── Endpoints ──
    public const string EndpointsEndpoints = "Endpoints.Endpoints";
    public const string EndpointsManualEndpoints = "Endpoints.ManualEndpoints";
    public const string EndpointsAllEndpoints = "Endpoints.AllEndpoints";

    // ── Mapping ──
    public const string MappingMapFrom = "Mapping.MapFrom";
    public const string MappingMapTo = "Mapping.MapTo";
    public const string MappingAllMappings = "Mapping.AllMappings";

    // ── Validation ──
    public const string ValidationValidatables = "Validation.Validatables";
    public const string ValidationValidators = "Validation.Validators";
    public const string ValidationAsyncBindings = "Validation.AsyncBindings";

    // ── Caching ──
    public const string CachingCacheables = "Caching.Cacheables";
    public const string CachingInvalidators = "Caching.Invalidators";

    // ── Messaging ──
    public const string MessagingHandlers = "Messaging.Handlers";
    public const string MessagingRequestHandlers = "Messaging.RequestHandlers";
    public const string MessagingSagas = "Messaging.Sagas";

    // ── Composition ──
    public const string CompositionServices = "Composition.Services";
    public const string CompositionStartupSteps = "Composition.StartupSteps";
    public const string CompositionModules = "Composition.Modules";
    public const string CompositionEventHandlers = "Composition.EventHandlers";

    // ── Configuration ──
    public const string ConfigurationConfigurations = "Configuration.Configurations";

    // ── Identity ──
    public const string IdentityPermissions = "Identity.Permissions";   // [assembly: Permission]
    public const string IdentityRoles = "Identity.Roles";
    public const string IdentityCustomPermissions = "Identity.CustomPermissions";

    // ── Jobs ──
    public const string JobsRecurringJobs = "Jobs.RecurringJobs";
    public const string JobsOneOffJobs = "Jobs.OneOffJobs";
    public const string JobsAllJobs = "Jobs.AllJobs";

    // ── Resource ──
    public const string ResourceResources = "Resource.Resources";
    public const string ResourceCrudModels = "Resource.CrudModels";
    public const string ResourceResolvedCrudModels = "Resource.ResolvedCrudModels";
    public const string ResourceQueries = "Resource.Queries";
    public const string ResourceEndpoints = "Resource.Endpoints";
    public const string ResourceActions = "Resource.Actions";
    public const string ResourceMutations = "Resource.Mutations";
    public const string ResourceOverrides = "Resource.Overrides";
    public const string ResourceMappings = "Resource.Mappings";
}
