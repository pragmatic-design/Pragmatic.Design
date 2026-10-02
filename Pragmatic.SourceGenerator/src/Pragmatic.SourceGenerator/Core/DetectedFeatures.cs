namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Snapshot of which Pragmatic runtime packages are referenced in the current compilation.
///     Computed once per compilation via FeatureDetector.
/// </summary>
/// <remarks>
///     A flag here is not free: every one costs a symbol lookup on every <c>Compilation</c> change.
///     Only add a flag when a feature actually gates on it — <c>Detect</c> is not an inventory of the
///     ecosystem. There is deliberately no flag for these, because nothing reads them and a flag nobody
///     reads is a standing cost (do not add them out of symmetry with the package list):
///     <c>HasPersistence</c>, <c>HasPatch</c>, <c>HasComments</c>, <c>HasControlPlane</c>,
///     <c>HasMessagingAuditing</c>, <c>HasMessagingSagas</c>, <c>HasMessagingJobs</c>,
///     <c>IsHostCompositionMode</c>.
/// </remarks>
internal sealed record DetectedFeatures
{
    public bool HasActions { get; init; }
    public bool HasValidation { get; init; }
    public bool HasCaching { get; init; }
    public bool HasMapping { get; init; }
    public bool HasEndpoints { get; init; }

    /// <summary>True when the host references Pragmatic.Endpoints.OpenApi, so it publishes the compile-time document.</summary>
    public bool HasEndpointsOpenApi { get; init; }

    /// <summary>True when the host references Scalar.AspNetCore, so the document gets an interactive reference in Development.</summary>
    /// <remarks>
    ///     A third-party package the framework does not reference: only the host's compilation can say
    ///     whether it is there, and the generated entry point names it only then.
    /// </remarks>
    public bool HasScalar { get; init; }
    public bool HasComposition { get; init; }
    public bool HasPersistenceEFCore { get; init; }

    /// <summary>Whether EF Core itself is referenced, with or without Pragmatic.Persistence.</summary>
    /// <remarks>
    ///     ⚠️ Not the same question as <see cref="HasPersistenceEFCore" />, and the difference
    ///     is load-bearing for anything that needs a <c>DbContext</c> rather than a Pragmatic one:
    ///     a project can map DTOs onto EF entities without using Pragmatic persistence at all,
    ///     and the mapping tests in this repository are exactly that shape.
    /// </remarks>
    public bool HasEfCore { get; init; }
    public bool HasI18n { get; init; }

    /// <summary>
    ///     The ASP.NET Core integration package, which is a separate reference from
    ///     <see cref="HasI18n" />. Host wiring lives there, so gating it on the base package emits a
    ///     using for an assembly the compilation may not have.
    /// </summary>
    public bool HasI18nAspNetCore { get; init; }

    /// <summary>
    ///     <c>Pragmatic.Documents.Markup</c>, where <c>AddPdxTemplates</c> lives: the host registers the
    ///     template sources its modules declare only when it can name the call.
    /// </summary>
    public bool HasPdxTemplates { get; init; }
    public bool HasResult { get; init; }
    public bool HasConfiguration { get; init; }
    public bool HasResilience { get; init; }
    public bool HasIdentityAspNetCore { get; init; }
    public bool HasMultiTenancy { get; init; }
    public bool HasFeatureFlags { get; init; }
    public bool HasDiscovery { get; init; }
    public bool HasTemporal { get; init; }

    /// <summary>True when the compilation references the temporal JSON behaviors registry (Pragmatic.Temporal.Json).</summary>
    public bool HasTemporalJson { get; init; }

    /// <summary>True when the compilation references the temporal ASP.NET Core integration (middleware, detection, binders).</summary>
    public bool HasTemporalAspNetCore { get; init; }

    /// <summary>True when the compilation references Pragmatic.Temporal.EFCore.</summary>
    /// <remarks>
    ///     Gates the temporal property registration: generated code may only name what its consumer
    ///     already references, and TemporalPropertyRegistry lives in that package.
    /// </remarks>
    public bool HasTemporalEfCore { get; init; }
    public bool HasAuthorization { get; init; }

    /// <summary>True when the compilation references Pragmatic.Events.</summary>
    /// <remarks>
    ///     Gates the in-memory dispatcher in the host: an operation that publishes resolves it whether
    ///     or not anybody handles what it publishes.
    /// </remarks>
    public bool HasEvents { get; init; }

    public bool HasEventsEFCore { get; init; }

    /// <summary>
    ///     <c>Pragmatic.Privacy.EFCore</c> is referenced: the subject registry is stored with EF Core, so
    ///     the context holding a <c>[DataSubject]</c> maps its tables.
    /// </summary>
    public bool HasPrivacyEFCore { get; init; }

    /// <summary>
    ///     <c>Pragmatic.Cryptography.EFCore</c> is referenced: a <c>ProtectedValue</c> property can be
    ///     stored, because the converter that maps it to its column exists.
    /// </summary>
    public bool HasCryptographyEFCore { get; init; }

    public bool HasMessaging { get; init; }
    public bool HasMessagingEFCore { get; init; }
    public bool HasMessagingChannels { get; init; }
    public bool HasMessagingRabbitMq { get; init; }
    public bool HasMessagingBatch { get; init; }
    public bool HasIdentityPersistence { get; init; }
    public bool HasJobs { get; init; }

    /// <summary>
    ///     True when the compilation references <c>Pragmatic.Jobs.EFCore</c>, so the generated DbContext
    ///     can name the two configurations the durable job store's tables need.
    /// </summary>
    /// <remarks>
    ///     Read where the decision is made: <c>[EnableJobPersistence]</c> maps <c>__Jobs</c> and
    ///     <c>__RecurringJobs</c> only when this is true, and reports <c>PRAG2508</c> otherwise — a
    ///     generated line naming a type the compilation has not got is a file the author cannot fix.
    /// </remarks>
    public bool HasJobsEFCore { get; init; }

    /// <summary>
    ///     Whether <c>Pragmatic.Notifications.EFCore</c> is referenced, so <c>__Notifications</c> can be
    ///     mapped into the boundary that declares <c>[StoresNotifications]</c>.
    /// </summary>
    public bool HasNotificationsEFCore { get; init; }

    /// <summary>True when the module references Pragmatic.Imaging, so a thumbnail can be derived.</summary>
    /// <remarks>
    ///     Read where the decision is made, not where it would be executed:
    ///     <c>[HasAttachments(ThumbnailMaxWidth = …)]</c> emits the derivation only when this is true,
    ///     rather than emitting a nullable capability and asking for it at run time. A capability that
    ///     is always null is indistinguishable from one that works.
    /// </remarks>
    public bool HasImaging { get; init; }
    public bool HasMigrations { get; init; }

    /// <summary>True when the host references Npgsql, so a typed NpgsqlConnection can be constructed.</summary>
    /// <remarks>
    ///     Pragmatic.Migrations references no driver and so could not name one: it asked at run time,
    ///     with <c>Type.GetType("Npgsql.NpgsqlConnection, Npgsql")</c>. Asked here instead, on the
    ///     compilation that does reference it, the answer is a compile-time fact.
    /// </remarks>
    public bool HasNpgsqlDriver { get; init; }

    /// <summary>True when the host references Microsoft.Data.SqlClient.</summary>
    public bool HasSqlServerDriver { get; init; }

    /// <summary>True when the host references Microsoft.Data.Sqlite.</summary>
    public bool HasSqliteDriver { get; init; }
    public bool HasNotifications { get; init; }

    /// <summary>True when the compilation references the JSON serialization seam (Pragmatic.Serialization.PragmaticJsonOptions).</summary>
    public bool HasSerialization { get; init; }

    /// <summary>True when the compilation references the personal-data classification attributes (Pragmatic.Privacy.Abstractions).</summary>
    public bool HasPrivacy { get; init; }

    /// <summary>
    ///     True when the compilation references the data-subject runtime (Pragmatic.Privacy), whose
    ///     <c>IPersonalDataSource</c> / <c>IErasureStep</c> / <c>IProcessingActivitySource</c> the
    ///     generated adapters implement.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="HasPrivacy" /> on purpose: the attributes live in a zero-dependency
    ///     package a module can reference on its own, and emitting an adapter against interfaces that
    ///     assembly cannot see would turn classifying a field into a build error.
    /// </remarks>
    public bool HasPrivacyRuntime { get; init; }

    /// <summary>True when <c>Pragmatic.Audit</c> is referenced, so generated code may write to the trail.</summary>
    /// <remarks>
    ///     Gates the read-access recording a query opts into with <c>[RecordAccess]</c>. Generated code
    ///     can only name what its own compilation references, and a module that classified data but does
    ///     not reference the audit package would otherwise get a build error out of an attribute.
    /// </remarks>
    public bool HasAudit { get; init; }

    /// <summary>
    ///     <c>Pragmatic.Audit.EFCore</c> is referenced: the host registers <c>AuditDbContext</c> and the trail
    ///     on the database whose migration creates the trail's tables.
    /// </summary>
    public bool HasAuditEFCore { get; init; }

    /// <summary>True when the compilation has an entry point (executable project).</summary>
    public bool IsHostMode { get; init; }

    /// <summary>The EF Core database provider detected from referenced assemblies.</summary>
    public EfCoreProvider EfCoreProvider { get; init; }

    public static DetectedFeatures None { get; } = new();
}
