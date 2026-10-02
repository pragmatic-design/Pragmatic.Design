using Casework.Intake.Host;
using Casework.Intake.Infrastructure.Authorization;
using Casework.Intake.Organisations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Email;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Internationalization;
using Pragmatic.Internationalization.Types;
using Pragmatic.Jobs;
using Pragmatic.Jobs.EFCore.Extensions;
using Pragmatic.Messaging.Entities;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.Jobs;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Migrations.Extensions;
using Pragmatic.MultiTenancy;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Storage;
using Pragmatic.Storage.Local;

await PragmaticApp.RunAsync(args, app =>
{
    // The languages this service speaks. The skeleton has no translation file yet, so the default is
    // given here: with no default from anywhere the host refuses to start rather than failing every
    // request, and that refusal is correct — a culture has to come from somewhere.
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS);
        i18n.Support(CultureCode.EnglishUS, CultureCode.FromString("it-IT"));
        // The module's own messages, from the folder its build copies beside the application. ⚠️ Without
        // this call the compile-time keys still exist and every lookup misses, leaving the key itself in
        // the answer — which reads like a message and is not one.
        i18n.AddJsonTranslations(Path.Combine(AppContext.BaseDirectory, "translations", "intake"));
        // The refusals too: title and detail from the error's MessageKey. It is what gives an
        // [Invariant]'s refusal a sentence of its own instead of one text shared by every rule.
        i18n.LocalizeProblemDetails();
    });

    // How this service talks to the other one. The transport is the host's choice and the module knows
    // nothing of it: RabbitMQ, from configuration.
    //
    // ⚠️ No retry and no delay around the first publish, on purpose: RabbitMQ connects in the background
    // and a publish issued meanwhile waits for it (ConnectWaitTimeout, 30s by default). The application
    // starts with its broker down, which is the behaviour a deployment wants and the one a wrapper here
    // would hide.
    // What this service's outbox can carry: the generated registry of the assembly that declares the
    // events. The generator emits one for every assembly with domain events — a publisher with no
    // handler included — so this line names generated code instead of a hand-written switch.
    //
    // ⚠️ It is called by hand because the host's composition discovers **modules**, and a contracts
    // assembly is deliberately not one: it declares the registration in
    // `[assembly: PragmaticMetadata(MessageHandlers, …)]` and the generated host walks that category
    // only for assemblies it discovered. So an application with a contracts project names it here — one
    // line, and a compile error if the name is wrong, where a hand-written list fails by dead-lettering a
    // message nobody was waiting for.
    //
    // Forgetting the line is a build warning rather than six red integration tests:
    // PRAG1699 names the assembly and the call, and goes quiet once this line is here.
    global::Casework.Intake.Contracts.Generated.PragmaticMessageHandlerRegistration
        .AddPragmaticMessageHandlers(app.Services);

    app.UseMessaging(msg =>
    {
        msg.UseRabbitMq(rabbit =>
        {
            rabbit.ConnectionString = app.Configuration["Messaging:RabbitMq:ConnectionString"]
                ?? throw new InvalidOperationException("Messaging:RabbitMq:ConnectionString is required.");
            rabbit.ConsumerPrefetchCount = 10;
        });

        // The outbox is already mapped and its pump already registered — [EnableOutbox] on the boundary
        // did that, and this call adds no wiring. What it does is tune it, and the three numbers are
        // this deployment's:
        //
        //  - two seconds between sweeps, because a verification request that waits five is a case that
        //    looks stuck to whoever opened it, and the row is cheap to look for;
        //  - fifty rows a batch, well above what one request writes and small enough that a backlog is
        //    drained in visible steps rather than in one transaction;
        //  - seven days of retention for a delivered row, which is this service's window for answering
        //    "was it sent, and when" without the table growing without bound. It is longer than the
        //    framework's three days on purpose: a case is worked over days, not minutes.
        //
        // ⚠️ Retention reaches the MessagingOptions the purge service resolves: every outbox option
        // EnableOutbox writes onto the builder is copied there by AddPragmaticMessaging.
        msg.EnableOutbox(outbox =>
        {
            outbox.PollingIntervalSeconds = 2;
            outbox.BatchSize = 50;
            outbox.Retention = TimeSpan.FromDays(7);
        });

        // Deduplication, because at-least-once means the transport may hand the same message over twice
        // — and it is a decision, so it is here and not a default.
        //
        // ⚠️ It is not what makes this application correct, and the handler says so: the store is
        // **in memory**, so a restart forgets every id it had claimed, and the key is the *message* id —
        // for an outbox row, that row's id — so two publishes of one event are two ids and no duplicate
        // at all. What it buys is the redelivery of the *same* delivery attempt, which is the common
        // case and worth spending nothing on. What makes a repeated answer harmless is
        // Case.AnsweredByEvent.
        msg.EnableIdempotency();

        // A handler that declares [Redelivery] needs somewhere to put the next attempt: this registers
        // the scheduler that keeps it, as a job, so a retry survives the process that scheduled it.
        // Without it the declaration falls through to dead-lettering immediately, which is the
        // fallback the attribute's own remark promises and not what it is for.
        msg.EnableScheduledMessages();
    });

    // Where a case's documents are kept. Local disk is this deployment's choice and the seam is
    // IFileStorage: Azure Blob or S3 would be a different factory here and nothing else.
    //
    // ⚠️ The root is composed with Path.Combine and never with a separator written here: the gate runs on
    // Windows and CI runs on ubuntu, and a "\" in a path is a literal character in a Linux file name. The
    // storage *key* is a different thing and is always composed with "/" — see CaseDocumentStorageKey.
    // How an organisation's mail leaves the building. ⚠️ No DefaultFrom, on purpose:
    // every message names its sender and the sender is the organisation's own address, so a default
    // here would mail every organisation's applicants from one place — and a reply would go to the
    // wrong people about somebody else's case. An organisation with no address sends nothing, and the
    // handler says so.
    app.UseEmail(email =>
    {
        var smtp = app.Configuration["Smtp:Host"];
        if (string.IsNullOrEmpty(smtp))
            email.UseNullTransport();
        else
            email.UseSmtp(server =>
            {
                server.Host = smtp;
                server.Port = int.TryParse(app.Configuration["Smtp:Port"], out var port) ? port : 587;
                server.UseSsl = true;
                server.Username = app.Configuration["Smtp:User"];
                server.Password = app.Configuration["Smtp:Password"];
            });
    });

    app.UseStorage(services => new LocalDiskFileStorage(
        app.Configuration["Storage:Root"] ?? Path.Combine(AppContext.BaseDirectory, "storage"),
        services.GetRequiredService<ILogger<LocalDiskFileStorage>>()));

    // The hourly deadline sweep. Two workers and a ten-second poll are this deployment's
    // numbers, not the framework's.
    //
    // ⚠️ Both EF Core calls are needed and they are not the same thing: `UseEfCore()` chooses the store
    // for the queue, `UseEfCorePersistence()` the one for the recurring definitions. And the tables they
    // use exist because [EnableJobPersistence] on the boundary asked for them — `UseEfCore()` fails the
    // start rather than falling back to memory, which is why the next line is not optional.
    app.UseJobs(jobs =>
    {
        jobs.WithWorkerCount(2);
        jobs.WithPollingInterval(10);
        jobs.UseEfCore();
        jobs.UseEfCorePersistence();
    });

    // The schema follows the entities: the runner diffs the desired schema against the live database and
    // applies it at startup. The organisations' own databases are brought along by the same two calls,
    // registered below as TheMigrationOfEveryDatabase so an operator or a test can read the report.
    app.UsePragmaticMigrations();

    // The API contract as the generator wrote it at compile time, in every environment.
    app.UseApiDocumentation();

    // Who the callers are: a token this service signs. `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` from
    // configuration — the call validates them at startup instead of accepting anything at run time.
    //
    // ⚠️ `Jwt:RequireSecurityStamp` is false in appsettings.json, and that is a decision and not a
    // relaxation: the security stamp is how a token is revoked, and it is revoked by rotating the stamp
    // of a stored account. Casework holds no accounts — it stores no password and has nothing to rotate
    // — so a stamp here would be a field nobody could ever change. What bounds a revoked session is the
    // token's lifetime, and that is the second of the two cases `JwtOptions.RequireSecurityStamp`
    // documents. A service that grew local accounts would turn it back on in the same commit.
    app.UseJwtAuthentication();

    // The tenant comes from the token — the `tenant_id` claim — and from nowhere else. A header, a route
    // segment or a subdomain is input the client controls, and an authenticated caller naming another
    // organisation's id is exactly the escalation the claim guard closes.
    //
    // ⚠️ The tenancy is the whole arrangement: the claim, a register of organisations on
    // the shared database, `RequireKnownTenant`, and a database per organisation for the ones whose row
    // names one. Each of the four is a line below and each says what it costs.
    app.UseMultiTenancy(tenancy =>
    {
        tenancy.UseClaim();
        tenancy.Services.Configure<MultiTenancyOptions>(options =>
        {
            options.RequireTenant = true;
            options.EnforceTenantClaim = true;
            options.EnforceTenantState = true;
            // On, because there is a register to check against: an id that names no organisation is a
            // 404 and not a silent write into a tenant nobody onboarded. ⚠️ With the framework's stock
            // in-memory store — which nothing writes to — this setting refuses every request, which is
            // why it needs the store below.
            options.RequireKnownTenant = true;
        });

        // The register: the organisations, read from the shared database. See the class for why it opens
        // that connection itself instead of going through the DbContext.
        tenancy.Services.AddSingleton<ITenantStore>(_ => new TheOrganisationsAreTheTenants(
            app.Configuration.GetConnectionString("Intake")
            ?? throw new InvalidOperationException("ConnectionStrings:Intake is required.")));

        // A database per organisation — and the shared schema for the ones without.
        //
        // ⚠️ <b>There is no DbContext registration to change, and a reader will look for one.</b>
        // `UseDbPerTenant` registers a `TenantConnectionInterceptor`, EF Core discovers it from DI, and
        // on every connection open it rewrites the connection string to the current tenant's database.
        // An organisation whose row has no connection string stays on the shared one with row-level
        // isolation — the two models are one deployment.
        //
        // ⚠️ Nothing here provisions anything. The framework does not create a database when a tenant is
        // first accessed: the first request of an organisation whose row names a database nobody created
        // fails with `3D000: database does not exist`. What provisions is explicit and below.
        tenancy.UseDbPerTenant(databases =>
        {
            databases.DefaultConnectionString = app.Configuration.GetConnectionString("Intake") ?? "";
            databases.ConnectionStringTemplate =
                app.Configuration["MultiTenancy:ConnectionStringTemplate"] ?? "";
        });

        // Who actually creates a database. ⚠️ Without this line the registered
        // provisioner is `NoOpTenantProvisioner`: `ProvisionAsync` returns, nothing is created, and the
        // first request of that organisation fails with `3D000: database does not exist` — a default
        // that is right for a deployment where a control plane owns the server, and wrong for a service
        // that onboards its own customers.
        tenancy.Services.UseAutoProvision<PostgresTenantProvisioner>();

        // And who puts this service's schema in it once it exists: the provisioner creates an empty
        // database and stops there. The implementation is in this assembly because the schema constant
        // is — see the class.
        tenancy.Services.AddSingleton<IProvisionTenantDatabases, TheDatabasesThisServiceMakes>();

        // And the run that brings every one of them to the current schema. The host already
        // does this at startup; registering it makes the same run available to an operator and to a
        // test, with its report returned instead of logged.
        tenancy.Services.AddSingleton<TheMigrationOfEveryDatabase>();
    });

    // What a caseworker may do. The role's name travels in the token; its permissions are listed in the
    // role's class and resolved here, so a token cannot claim a permission it was not granted.
    // Two roles: whoever works the cases, and whoever adds the organisations that bring
    // them. Separate on purpose — a caseworker who could onboard could give themselves a tenant.
    app.UseAuthorization(authz =>
    {
        authz.MapRole<CaseworkerRole>();
        authz.MapRole<ServiceOperatorRole>();
    });
}).ConfigureAwait(false);
