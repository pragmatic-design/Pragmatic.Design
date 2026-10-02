using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Detects which Pragmatic runtime packages are referenced in the current compilation.
///     Called once per compilation via CompilationProvider — never per syntax node.
/// </summary>
/// <remarks>
///     Every probe below runs again on every <c>Compilation</c> change, so a probe whose flag nothing
///     reads is pure waste. There is no probe for <c>HasPersistence</c>, <c>HasPatch</c>, <c>HasComments</c>,
///     <c>HasControlPlane</c>, <c>HasMessagingAuditing</c>, <c>HasMessagingSagas</c>,
///     <c>HasMessagingJobs</c> or <c>IsHostCompositionMode</c> for that reason — see
///     <see cref="DetectedFeatures" />. Add one only together with the feature that reads it.
/// </remarks>
internal static class FeatureDetector
{
    public static DetectedFeatures Detect(Compilation compilation) => new()
    {
        HasActions = TypeExists(compilation, "Pragmatic.Actions.Attributes.DomainActionAttribute"),
        HasValidation = TypeExists(compilation, "Pragmatic.Validation.Attributes.ValidationAttribute"),
        HasCaching = TypeExists(compilation, "Pragmatic.Caching.Attributes.CacheableAttribute"),
        HasMapping = TypeExists(compilation, "Pragmatic.Mapping.Attributes.MapFromAttribute`1"),
        HasEndpoints = TypeExists(compilation, "Pragmatic.Endpoints.Attributes.EndpointAttribute"),
        HasEndpointsOpenApi = TypeExists(compilation, "Pragmatic.Endpoints.OpenApi.ApiDocumentationMapper"),
        HasScalar = TypeExists(compilation, "Scalar.AspNetCore.ScalarEndpointRouteBuilderExtensions"),
        HasComposition = CompositionDetector.IsCompositionReferenced(compilation),
        HasPersistenceEFCore = TypeExists(compilation, "Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute"),
        HasEfCore = TypeExists(compilation, "Microsoft.EntityFrameworkCore.DbContext"),
        HasI18n = TypeExists(compilation, "Pragmatic.Internationalization.Attributes.TranslationKeysAttribute"),
        HasI18nAspNetCore = TypeExists(compilation, "Pragmatic.Internationalization.AspNetCore.Extensions.I18NBuilder"),
        HasPdxTemplates = TypeExists(compilation, "Pragmatic.Documents.Markup.PdxTemplatesServiceCollectionExtensions"),
        HasResult = TypeExists(compilation, "Pragmatic.Result.IError"),
        HasConfiguration = TypeExists(compilation, "Pragmatic.Configuration.ConfigurationAttribute"),
        HasResilience = TypeExists(compilation, "Pragmatic.Resilience.Attributes.ResiliencePolicyAttribute"),
        // Anchored on the permission *handler*, which is host-side and stays in Identity.AspNetCore.
        // The requirement it evaluates moved to Pragmatic.Endpoints.AspNetCore, which every
        // boundary with an endpoint references — so anchoring on that type would answer "yes" for
        // every module and the flag would stop meaning what its readers need: whether this compilation
        // can wire the identity half of the host.
        HasIdentityAspNetCore = TypeExists(compilation, "Pragmatic.Identity.Authorization.PragmaticPermissionHandler"),
        // Anchored on a type the emitted wiring itself names, not on ITenantContext: that one lives
        // in Pragmatic.Abstractions, which every application references, so the flag was true even
        // where the MultiTenancy package was absent and the generated host could not compile.
        HasMultiTenancy = TypeExists(compilation, "Pragmatic.MultiTenancy.InMemoryTenantStore"),
        HasFeatureFlags = TypeExists(compilation, "Pragmatic.FeatureFlags.FeatureFlagServiceCollectionExtensions"),
        HasDiscovery = TypeExists(compilation, "Pragmatic.Discovery.Abstractions.IDiscoveryService"),
        HasTemporal = TypeExists(compilation, "Pragmatic.Temporal.Clock.SystemClock"),
        HasTemporalJson = TypeExists(compilation, "Pragmatic.Temporal.Json.Behaviors.TemporalJsonBehaviorRegistry"),
        HasTemporalAspNetCore = TypeExists(compilation, "Pragmatic.Temporal.AspNetCore.Middleware.TemporalContextMiddleware"),
        HasTemporalEfCore = TypeExists(compilation, "Pragmatic.Temporal.EntityFrameworkCore.Conventions.TemporalPropertyRegistry"),
        HasAuthorization = TypeExists(compilation, "Pragmatic.Authorization.PragmaticBuilderAuthorizationExtensions"),
        HasEvents = TypeExists(compilation, "Pragmatic.Events.IDomainEventDispatcher"),
        // The raising interceptor, which is what the generated registration names.
        HasEventsEFCore = TypeExists(compilation, "Pragmatic.Events.EFCore.LifecycleEventsInterceptor"),
        HasPrivacyEFCore = TypeExists(compilation, "Pragmatic.Privacy.EFCore.PrivacyDbContext"),
        // Keyed on the converter itself, which is the thing the generated configuration names: a probe
        // for anything else could pass while the type the emitted line needs is absent.
        HasCryptographyEFCore = TypeExists(compilation, ProtectedValueType.ConverterFullName),
        HasMessaging = TypeExists(compilation, "Pragmatic.Messaging.Attributes.MessageHandlerAttribute"),
        HasMessagingEFCore = TypeExists(compilation, "Pragmatic.Messaging.EFCore.EfCoreOutboxSource"),
        HasMessagingChannels = TypeExists(compilation, "Pragmatic.Messaging.Channels.ChannelTransport"),
        HasMessagingRabbitMq = TypeExists(compilation, "Pragmatic.Messaging.RabbitMQ.RabbitMqTransport"),
        HasMessagingBatch = TypeExists(compilation, "Pragmatic.Messaging.Batch.IBatchProgressStore"),
        // Keyed on an entity of the package, not on a shape such as an inheritance base: a marker
        // must outlive the shape it happens to mark.
        // Non-generic on purpose: GetTypeByMetadataName needs backtick arity for a generic type,
        // and ExternalIdentityRecord<TKey> silently resolved to null when tried without it.
        HasIdentityPersistence = TypeExists(compilation, "Pragmatic.Identity.Persistence.Entities.RolePermission"),
        HasJobs = TypeExists(compilation, "Pragmatic.Jobs.Attributes.RecurringJobAttribute"),
        // Keyed on the configuration the generated DbContext applies, which is the question the
        // emission asks: can this compilation name it?
        HasJobsEFCore = TypeExists(compilation, "Pragmatic.Jobs.EFCore.Entities.JobEntityTypeConfiguration"),
        HasNotificationsEFCore = TypeExists(compilation, "Pragmatic.Notifications.EFCore.NotificationDbContext"),
        // Keyed on ImagePipeline rather than on the package name: it is the type the generated
        // derivation actually calls, so the probe answers the question the emission asks.
        HasImaging = TypeExists(compilation, "Pragmatic.Imaging.ImagePipeline"),
        HasMigrations = TypeExists(compilation, "Pragmatic.Migrations.Schema.SchemaVersion"),
        HasNpgsqlDriver = TypeExists(compilation, "Npgsql.NpgsqlConnection"),
        HasSqlServerDriver = TypeExists(compilation, "Microsoft.Data.SqlClient.SqlConnection"),
        HasSqliteDriver = TypeExists(compilation, "Microsoft.Data.Sqlite.SqliteConnection"),
        HasPrivacy = TypeExists(compilation, AttributeNames.PrivacyPersonalData),
        HasPrivacyRuntime = TypeExists(compilation, "Pragmatic.Privacy.IPersonalDataSource"),
        HasAudit = TypeExists(compilation, "Pragmatic.Audit.IAuditTrail"),
        HasAuditEFCore = TypeExists(compilation, "Pragmatic.Audit.EFCore.AuditDbContext"),
        HasNotifications = TypeExists(compilation, "Pragmatic.Notifications.INotificationService"),
        HasSerialization = TypeExists(compilation, "Pragmatic.Serialization.PragmaticJsonOptions"),
        IsHostMode = CompositionDetector.IsHostProject(compilation),
        EfCoreProvider = DetectEfCoreProvider(compilation),
    };

    private static bool TypeExists(Compilation compilation, string fullyQualifiedName)
        => compilation.GetTypeByMetadataName(fullyQualifiedName) is not null;

    /// <summary>
    /// Detects the EF Core database provider from referenced assemblies.
    /// Priority: PostgreSQL > SQL Server > SQLite > Generic.
    /// </summary>
    private static EfCoreProvider DetectEfCoreProvider(Compilation compilation)
    {
        // Npgsql.EntityFrameworkCore.PostgreSQL
        if (TypeExists(compilation, "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions"))
            return EfCoreProvider.PostgreSql;

        // Microsoft.EntityFrameworkCore.SqlServer
        if (TypeExists(compilation, "Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsBuilderExtensions"))
            return EfCoreProvider.SqlServer;

        // Microsoft.EntityFrameworkCore.Sqlite
        if (TypeExists(compilation, "Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions"))
            return EfCoreProvider.Sqlite;

        return EfCoreProvider.Generic;
    }
}
