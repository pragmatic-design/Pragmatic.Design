using Microsoft.EntityFrameworkCore;
using Pragmatic.Agent.Client;
using Pragmatic.Authorization.Configuration;
using Pragmatic.Composition.Hosting;
using Pragmatic.Endpoints.Mcp;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Pragmatic.Messaging.Auditing;
using Pragmatic.Messaging.AzureServiceBus;
using Pragmatic.Messaging.Channels;
using Pragmatic.Messaging.Dashboard;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Storage;
using Pragmatic.Storage.Local;
using Pragmatic.Temporal;
using Showcase.Billing;
using Showcase.Billing.Infrastructure.Authorization;
using Showcase.Booking;
using Showcase.Booking.Infrastructure.Notifications;
using Showcase.Booking.Infrastructure.Authorization;
using Showcase.Catalog;
using Showcase.Catalog.Infrastructure.Authorization;
using Pragmatic.Internationalization;
using Pragmatic.Jobs;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Notifications;
using Pragmatic.Notifications.EFCore;
using Pragmatic.Notifications.Email;
using Showcase.Host;
using Showcase.Host.Authorization;

using Showcase.Accounts.Generated;

await PragmaticApp.RunAsync(args, app =>
{
    // Maintenance mode — 503 + admin panel during DB init, SSE progress streaming
    // Pragmatic Agent — coordination daemon for config push, deploy, health
    // In Development: auto-starts the Agent daemon as child process
    app.UseAgent();

    app.UseMaintenanceMode();

    // The audit trail messaging records onto is registered by the generated host, in the App database
    // beside the [Audited] Amenity: a producer that needs its entry to commit with the change can enlist
    // in the same transaction.

    // MCP — expose [McpTool] endpoints as Model Context Protocol tools at /mcp.
    // /mcp requires authorization by default; the self-call forwards Authorization only.
    // This app carries identity in headers (dev HeaderUserMiddleware), so opt in to forwarding
    // them to the self-call — in production prefer a bearer token over client-asserted headers.
    app.UseMcp(o =>
    {
        o.ForwardedHeaders.Add("X-User-Id");
        o.ForwardedHeaders.Add("X-User-Name");
        o.ForwardedHeaders.Add("X-User-Permissions");
        o.ForwardedHeaders.Add("X-Tenant-Id");
    });

    // Pragmatic Migrations — declarative schema diff, replaces EF Core migrations entirely
    // Creates tables from scratch on empty DB, applies diff on existing DB
    app.UsePragmaticMigrations();

    // The compile-time OpenAPI document in every environment, not only in Development (the generated
    // host publishes it there, with Scalar over it, without being asked).
    app.UseApiDocumentation();

    // Multi-tenancy — resolve tenant from HTTP header
    app.UseMultiTenancy(mt => mt.UseHeader());

    // Background jobs — recurring + delayed scheduling
    app.UseJobs(jobs =>
    {
        jobs.WithWorkerCount(2);
        jobs.WithPollingInterval(10);
    });

    // Temporal — the chain's head office is in Rome, so "the business timezone" is Europe/Rome and not
    // wherever the server happens to run. It is what [ToBusinessTimezone] and [FromBusinessTimezone]
    // convert against; leaving it at its UTC default makes both attributes no-ops that look wired.
    // DefaultTimeZone stays UTC on purpose: a caller who asks for no zone gets the stored instant.
    app.UseTemporal(temporal => temporal.UseBusinessTimeZone("Europe/Rome"));

    // Internationalization — runtime providers for error localization + frontend endpoint
    app.UseI18N(i18n =>
    {
        i18n.AddJsonTranslations(
            Path.Combine(app.Environment.ContentRootPath, "localization"),
            watchForChanges: app.Environment.IsDevelopment());
        i18n.LocalizeProblemDetails();
    });

    // The generated per-user culture provider, off unless asked for. It reads AppUser.PreferredCulture
    // at priority 200 — above the tenant default, below an explicit ?culture= on the request — and
    // registers the AppUserResolver it reads through.
    app.UseUserCulture();

    // Notifications — unified pipeline with SMTP email channel (delegates to Pragmatic.Email)
    app.UseNotifications(n =>
    {
        // Without a resolver, NotificationRecipient.User(...) has no way to become an address and
        // every notification addressed to a guest fails.
        n.UseRecipientResolver<GuestRecipientResolver>();

        n.AddSmtp(
            sender =>
            {
                sender.SenderAddress = app.Configuration["Smtp:SenderAddress"] ?? "noreply@showcase.pragmatic.design";
                sender.SenderName = app.Configuration["Smtp:SenderName"] ?? "Showcase Hotel";
            },
            transport =>
            {
                transport.Host = app.Configuration["Smtp:Host"] ?? "localhost";
                transport.Port = int.TryParse(app.Configuration["Smtp:Port"], out var p) ? p : 587;
                transport.UseSsl = !app.Environment.IsDevelopment();
                transport.MaxConnections = 3;
            });

        // What was sent, kept. ConnectionStrings:App is the key ShowcaseAppDatabase uses — the database
        // the Booking boundary lives in, which is the boundary that declares [StoresNotifications], so
        // the table is created and migrated with the application's own.
        //
        // ⚠️ Without that declaration this call would wire a store whose table does not exist: a composed
        // host creates one context per declared database and nothing else.
        n.UseEfCoreStore(db => db.UseNpgsql(
            app.Configuration.GetConnectionString("App")
            ?? throw new InvalidOperationException("ConnectionStrings:App is required.")));
    });

    // Messaging — transport is config-driven: Azure Service Bus (real broker or local
    // emulator) when configured, otherwise the in-process Channels transport.
    var messagingTransport = app.Configuration["Pragmatic:Messaging:Transport"];
    app.UseMessaging(msg =>
    {
        if (string.Equals(messagingTransport, "AzureServiceBus", StringComparison.OrdinalIgnoreCase))
        {
            msg.UseAzureServiceBus(sb =>
            {
                sb.ConnectionString = app.Configuration["Pragmatic:Messaging:AzureServiceBus:ConnectionString"]
                    ?? throw new InvalidOperationException("Pragmatic:Messaging:AzureServiceBus:ConnectionString is required.");
                // The emulator has no management API (entities come from its Config.json);
                // real namespaces may be IaC-provisioned — auto-create degrades gracefully.
                sb.AutoCreateEntities = app.Configuration.GetValue(
                    "Pragmatic:Messaging:AzureServiceBus:AutoCreateEntities", defaultValue: true);
            });
        }
        else
        {
            msg.UseChannels(channels =>
            {
                channels.Capacity = 1000;
                channels.ConsumerCount = 2;
            });
        }

        // Durable sagas: CheckInSaga state persists in BookingDbContext (__SagaInstances/__SagaSteps).
        // Opt-in is entirely the [EnableSagaPersistence] attribute on BookingBoundary — no host call needed.

        // Messaging is now a producer on the framework's one audit trail rather than the owner of a
        // trail of its own. The trail itself is registered below; there is no payload option because
        // there is no payload field -- storing the serialized message is what put personal data in the
        // trail this replaces.
        msg.EnableAuditing();

        // Ops dashboard (API + panel) — config-driven, off by default in production without a key
        if (app.Configuration.GetValue("Pragmatic:Messaging:Dashboard:Enabled", defaultValue: false))
        {
            msg.EnableDashboard(dash =>
                dash.ApiKey = app.Configuration["Pragmatic:Messaging:Dashboard:ApiKey"]);
        }
    });

    // Authentication — config-driven: JWT for production, NoOp for development
    var jwtKey = app.Configuration["Jwt:Key"];
    if (!string.IsNullOrEmpty(jwtKey))
    {
        app.UseJwtAuthentication(jwt =>
        {
            jwt.SigningKey = jwtKey;
            jwt.Issuer = app.Configuration["Jwt:Issuer"] ?? "https://showcase.pragmatic.design";
            jwt.Audience = app.Configuration["Jwt:Audience"];
            jwt.TokenExpiration = TimeSpan.FromHours(
                double.TryParse(app.Configuration["Jwt:ExpirationHours"], out var h) ? h : 1);
        });
    }
    else
    {
        app.UseAuthentication<NoOpAuthenticationHandler>("PragmaticDefault");
    }

    // Authorization — compose application roles from module definitions
    app.UseAuthorization(authz =>
    {
        // Endpoints require authentication by default through
        // PragmaticEndpointsOptions.RequireAuthorizationByDefault (true), applied at the generated root
        // group; [AllowAnonymous] opts a single endpoint out.

        // Trust baked "permission" claims (stateless opt-in). The default is server-side resolution
        // (TrustPermissionClaims=false): permissions come from role/group expansion, not from claims.
        // The Showcase asserts identity via client headers (dev HeaderUserMiddleware / X-User-Permissions,
        // and the same header forwarded to the MCP self-call), so it intentionally opts in — a real
        // production app should leave this false and drive authority from roles for immediate revocation.
        authz.TrustPermissionClaims = true;

        // ── Application roles (IRole, composed from module IRoleDefinitions) ─

        // Admin: full system access
        authz.MapRole<ShowcaseAdmin>();                                         // "*"

        // Booking manager: booking ops + catalog read for context
        authz.MapRole<BookingManagerRole>(r => r
            .IncludeDefinition<BookingOperator>()                               // reservation.*, guest.*, guestprefs.*
            .IncludeDefinition<CatalogReader>());                               // + catalog read

        // Catalog viewer: read-only catalog
        authz.MapRole<CatalogViewerRole>(r => r
            .IncludeDefinition<CatalogReader>());                               // amenity.read, property.read, ...

        // Catalog editor: full catalog CRUD
        authz.MapRole<CatalogEditorRole>(r => r
            .IncludeDefinition<CatalogEditor>());                               // catalog.*

        // Receptionist: explicit booking perms (no delete) + catalog read
        // Uses explicit permissions instead of wildcard + WithoutPermissions (which is unsafe)
        authz.MapRole<ReceptionistRole>(r => r
            .WithPermissions(
                BookingPermissions.Reservation.Read,
                BookingPermissions.Reservation.Create,
                BookingPermissions.Reservation.Update)
            .WithPermissions(BookingPermissions.Guest.All)                       // guest.*
            .WithPermissions(BookingPermissions.GuestPreferences.All)            // guestpreferences.*
            .IncludeDefinition<CatalogReader>());                               // + catalog read

        // Billing clerk: the grant is on the role itself, spread from the two modules' [PermissionSet]
        // lists — so the role registry answers it too, and not only the store built here.
        authz.MapRole<BillingClerkRole>();                                      // billing work + booking read

        // Finance manager: billing + L2 custom refund permission
        authz.MapRole<FinanceManagerRole>(r => r
            .WithPermissions(BillingPermissions.All)                            // billing.*
            .WithPermissions(BillingPermissions.Invoice.Refund)                 // L2 custom
            .IncludeDefinition<BookingReader>());                               // + booking read

        // Auditor: read-only across ALL boundaries (string name — one example)
        authz.MapRole("auditor", r => r
            .WithOperation<BookingBoundary>(CrudOperation.Read)                 // "booking.*.read"
            .WithOperation<BillingBoundary>(CrudOperation.Read)                 // "billing.*.read"
            .WithOperation<CatalogBoundary>(CrudOperation.Read));               // "catalog.*.read"

        // ── Groups ──────────────────────────────────────────────────────────
        authz.MapGroup<CustomerCareGroup>();                                    // booking-manager + catalog-viewer

        // ── ABAC (instance-level authorization) ─────────────────────────────
        authz.AddResourceAuthorizer<InvoiceAuthorizer, Showcase.Billing.Actions.RefundInvoiceAction>();

        // ── JSON seeding (additional roles from file) ───────────────────────
        authz.SeedFromJson();                                                   // front-desk, revenue-manager

        // ── Caching ─────────────────────────────────────────────────────────
        authz.UsePermissionCache(TimeSpan.FromMinutes(5));
    });

    // Logging — Console + daily rolling file
    var logDir = Path.Combine(app.Environment.ContentRootPath, "logs");
    app.UseLogging(log =>
    {
        log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment());
        log.AddFile(
            Path.Combine(logDir, "showcase-{Date}.log"),
            config =>
            {
                config.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Debug;
                config.Formatting.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
                config.Formatting.UseUtcTimestamp = true;
            });
    });

    // File storage — LocalDisk for dev/demo; swap for Azure/S3 in prod
    app.UseStorage(sp =>
    {
        var basePath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        var logger = sp.GetRequiredService<ILogger<LocalDiskFileStorage>>();
        return new LocalDiskFileStorage(basePath, logger);
    });
}).ConfigureAwait(false);
