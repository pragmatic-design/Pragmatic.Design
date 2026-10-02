using Casework.Verify.Host;
using Casework.Verify.Infrastructure.Authorization;
using Casework.Verify.Organisations;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Entities;
using Pragmatic.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Internationalization;
using Pragmatic.MultiTenancy;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Internationalization.Types;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Migrations.Extensions;

await PragmaticApp.RunAsync(args, app =>
{
    // The same two languages as Intake: an outcome's wording reaches an applicant through Intake's
    // letter, but a refusal from this service's own API is still read by somebody.
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS);
        i18n.Support(CultureCode.EnglishUS, CultureCode.FromString("it-IT"));
        i18n.LocalizeProblemDetails();
    });

    // What this service's outbox can carry — the answer it publishes. The generated registry of the
    // contracts assembly, for the same reason Intake calls its own: the events are declared
    // there, so that is where the generator emits the registry, and the host's composition discovers
    // modules rather than every assembly that declares messaging metadata. Forgetting this line is
    // PRAG1699 at build time instead of a message that never arrives.
    global::Casework.Verify.Contracts.Generated.PragmaticMessageHandlerRegistration
        .AddPragmaticMessageHandlers(app.Services);

    // Both ends of the same transport now: this service consumes the request and publishes the answer.
    // The handler's queue is bound by the consumer service this call registers; without it the transport
    // stays disconnected and nothing is ever delivered.
    app.UseMessaging(msg => msg.UseRabbitMq(rabbit =>
    {
        rabbit.ConnectionString = app.Configuration["Messaging:RabbitMq:ConnectionString"]
            ?? throw new InvalidOperationException("Messaging:RabbitMq:ConnectionString is required.");
        rabbit.ConsumerPrefetchCount = 10;
    }));

    app.UsePragmaticMigrations();

    app.UseApiDocumentation();

    // Who calls this service's own API: a token it signs and validates, held by a system — the console
    // or the process that performs a verification and records what it found. No users and no accounts,
    // so `Jwt:RequireSecurityStamp` is false in appsettings.json for the same reason as Intake's: the
    // stamp is revocation, revocation rotates a stored account's stamp, and there is no store here. The
    // token's lifetime is what bounds a revoked session.
    app.UseJwtAuthentication();

    // A verification belongs to the organisation whose case asked for it, and this service's rows are
    // tenant-filtered fail-closed — so a caller with no tenant sees nothing at all. The tenant comes
    // from the token, as in Intake: a header would be input the caller controls.
    //
    // ⚠️ This is also the second half of what makes the handler work: the transport restores the
    // originating tenant into the consume scope, so the row is written under the right organisation, and
    // it is *this* resolution that makes the same row readable afterwards over HTTP. Neither half is
    // enough alone, and neither is visible in the other's code.
    app.UseMultiTenancy(tenancy =>
    {
        tenancy.UseClaim();
        tenancy.Services.Configure<MultiTenancyOptions>(options =>
        {
            options.RequireTenant = true;
            options.EnforceTenantClaim = true;
            options.EnforceTenantState = true;
            // On, because this service has a register of its own to check against.
            options.RequireKnownTenant = true;
        });

        // This service's own register, on its own shared database. Not Intake's: each service knows
        // which organisations it serves and where their rows are.
        tenancy.Services.AddSingleton<ITenantStore>(_ => new TheOrganisationsThisServiceServes(
            app.Configuration.GetConnectionString("Verify")
            ?? throw new InvalidOperationException("ConnectionStrings:Verify is required.")));

        // A database per organisation here too — and this is the harder half: a consumer has
        // no request, so the tenant comes from the message (the transport carries it in a header and the
        // subscription binder restores it into the consume scope), and the connection interceptor routes
        // the write from there. Neither the handler nor the operation knows any of it.
        tenancy.UseDbPerTenant(databases =>
        {
            databases.DefaultConnectionString = app.Configuration.GetConnectionString("Verify") ?? "";
            databases.ConnectionStringTemplate =
                app.Configuration["MultiTenancy:ConnectionStringTemplate"] ?? "";
        });

        // This service makes its own databases, because nobody else may: an onboarding
        // that crossed the bus is the only thing that creates one here. ⚠️ Without the line below the
        // registered provisioner is `NoOpTenantProvisioner` — `ProvisionAsync` returns and nothing is
        // created.
        tenancy.Services.UseAutoProvision<PostgresTenantProvisioner>();
        tenancy.Services.AddSingleton<IProvisionTenantDatabases, TheDatabasesThisServiceMakes>();

        // And the run that brings every one of them to the current schema. The host already
        // does this at startup; registering it makes the same run available to an operator and to a
        // test, with its report returned instead of logged.
        tenancy.Services.AddSingleton<TheMigrationOfEveryDatabase>();
    });

    // What the service token may do: read a verification and answer it. Not create one — a verification
    // exists because a case asked for it, over the bus.
    app.UseAuthorization(authz => authz.MapRole<VerificationServiceRole>());
}).ConfigureAwait(false);
