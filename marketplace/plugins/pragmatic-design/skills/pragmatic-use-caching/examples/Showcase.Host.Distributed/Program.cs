using Pragmatic.Agent.Client;
using Pragmatic.Agent.Discovery;
using Pragmatic.Caching.Redis;
using Pragmatic.Composition.Hosting;
using Pragmatic.Configuration.Extensions;
using Pragmatic.Documents.Markup;
using Pragmatic.Internationalization;
using Pragmatic.FeatureFlags;
using Pragmatic.FeatureFlags.Configuration;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Pragmatic.Storage;
using Pragmatic.Storage.Local;
using Pragmatic.Temporal;
using Showcase.Booking;
using Showcase.Booking.Infrastructure.Authorization;
using Showcase.Host.Distributed;

await PragmaticApp.RunAsync(args, app =>
{
    // Database — create tables automatically in development
    if (app.Environment.IsDevelopment())
        app.UseDatabaseEnsureCreated();

    // Multi-tenancy — resolve tenant from HTTP header
    app.UseMultiTenancy(mt => mt.UseHeader());

    // Temporal — the same head office as the monolith. It serves the same Booking module and the same
    // DTOs, so leaving the business timezone at its UTC default here would make [ToBusinessTimezone]
    // answer differently depending on which host you asked — which is worse than not having it.
    app.UseTemporal(temporal => temporal.UseBusinessTimeZone("Europe/Rome"));

    // Coordination backbone — the Pragmatic Agent (local daemon, KV + gossip). Replaces the former
    // SignalR control plane: IControlPlane, config/flag/tenant stores and cluster leadership are all
    // Agent-backed. If the daemon is unreachable the host degrades to L0 (monolith) mode.
    app.UseAgent();

    // Cluster-singleton demo — only the elected leader host runs the guarded work (IClusterLeadership,
    // the Agent KV-lease election). Reference consumer of the primitive; harmless single-host no-op in L0.
    app.Services.AddHostedService<ClusterSingletonDemo>();

    // Where this host's topology is published. The generated composition calls AddDiscovery(), whose
    // backend keeps it in memory — which in this topology means each host knows only itself. Through
    // the Agent it is stored and gossip-replicated, so the two hosts can see one another.
    //
    // Nothing to remove first: UseAgentDiscovery() replaces the backend itself (RemoveAll, then add),
    // which is what a package override should do.
    app.Services.UseAgentDiscovery();

    // Cache invalidation across hosts. This topology is the reason it exists: [Cacheable] keeps an
    // in-process copy per host, and an invalidation run on one of them reaches its own copy and
    // nobody else's — so the other host serves the old answer for the entry's whole duration, five
    // minutes for api/properties/search.
    //
    // ⚠️ Conditional, and that is not defensiveness: a single host does not need it, and without the
    // call nothing changes — the stack does not look for a broadcast at runtime, the host declares
    // one. Registered here because the callback runs after the generated AddPragmaticCaching(),
    // which is what this decorates.
    var redis = app.Configuration.GetConnectionString("Redis");
    if (!string.IsNullOrWhiteSpace(redis))
        app.Services.AddRedisCacheInvalidationBroadcast(redis);

    // Configuration services — the resolver and the stores the admin surface reads through.
    //
    // ⚠️ Needed because AccountsModule imports [UsePackage<ConfigurationManagementPackage>], and a
    // module's package becomes a requirement of **every** host that includes that module. The
    // monolith calls it from ShowcaseStartupStep — nothing generated calls it. This host has no such
    // step, so the package's actions found no IConfigurationResolver and container validation stopped
    // it — with nothing at compile time saying an import over there had made this host's job bigger.
    app.Services.AddPragmaticConfiguration();

    // Feature flags from configuration. The monolith seeds them programmatically into the in-memory
    // store on startup, which its own comment calls a demo: this host reads the "FeatureFlags"
    // section instead, so an operator turns one on without a deployment.
    //
    // ⚠️ No RemoveAll<IFeatureFlagStore>() in front of it: the generated AddPragmaticFeatureFlags()
    // has already claimed the slot and this callback runs after it, so AddConfigurationFeatureFlagStore()
    // replaces the registration rather than adding one. A TryAdd here would do nothing, and a caller's
    // explicit choice has to win.
    app.Services.AddConfigurationFeatureFlagStore();

    // Authentication — development identity from X-User-* headers.
    //
    // ⚠️ Not a bare UseAuthentication<NoOpAuthenticationHandler>(…): that handler reads no credential
    // of its own, it honours whatever HeaderUserMiddleware put on the context, and the monolith adds
    // that middleware by hand in its startup step. This host has no startup step, so without it every
    // request would be 401. UseDevelopmentIdentity() is the one call that registers all three pieces.
    app.UseDevelopmentIdentity();

    // Authorization — compose from module definitions
    app.UseAuthorization(authz =>
    {
        // As in the monolith: this demo carries permissions in X-User-Permissions, so it opts in to
        // trusting them. A real application leaves this false and drives authority from roles, so
        // revocation is immediate.
        authz.TrustPermissionClaims = true;

        authz.MapRole("booking-manager", r => r
            .IncludeDefinition<BookingOperator>());
        authz.MapGroup("customer-care", g => g.WithRoles("booking-manager"));
        authz.UsePermissionCache(TimeSpan.FromMinutes(5));
    });

    // Logging — Console only for demo
    app.UseLogging(log =>
    {
        log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment());
    });

    // File storage
    app.UseStorage(sp =>
    {
        var basePath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var logger = sp.GetRequiredService<ILogger<LocalDiskFileStorage>>();
        return new LocalDiskFileStorage(basePath, logger);
    });
}).ConfigureAwait(false);

/// <summary>
///     Named so a test can boot this host.
/// </summary>
/// <remarks>
///     Top-level statements compile to an <c>internal</c> <c>Program</c>, which
///     <c>WebApplicationFactory&lt;TEntryPoint&gt;</c> in another assembly cannot name. Without this
///     declaration no test could start this host.
/// </remarks>
public partial class Program;
