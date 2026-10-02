using Invoicing.Billing.Infrastructure.Authorization;
using Invoicing.Billing.Infrastructure.Jobs;
using Pragmatic.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Documents.Markup;
using Invoicing.Billing;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Identity.Oidc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Email;
using Pragmatic.Internationalization;
using Pragmatic.Jobs;
using Pragmatic.Jobs.EFCore.Extensions;
using Pragmatic.Storage;
using Pragmatic.Storage.Local;
using Pragmatic.Internationalization.Types;
using Pragmatic.Migrations.Extensions;
using Pragmatic.MultiTenancy;

await PragmaticApp.RunAsync(args, app =>
{
    // The two languages the application speaks, its messages, and whose language an answer is in: the one
    // the request names, else the default.
    //
    // There is deliberately **no** `app.UseUserCulture()`. That provider reads a `PreferredCulture`
    // profile property off a [PragmaticUser] entity, and Invoicing keeps no user row at all — the identity
    // is the token from each company's provider. So the caller's language comes from `Accept-Language` and
    // nowhere else, and this is the one place where this example is simpler than Time off for a reason
    // that is not laziness.
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS);
        i18n.Support(CultureCode.EnglishUS, CultureCode.FromString("it-IT"));
        // One call per module, each on that module's own folder. Beside the application, where the
        // modules' builds copy their files: a relative path would be read from wherever the process
        // happened to be started.
        // ⚠️ Two things are silent here, and both were measured. Without these calls the compile-time keys
        // still exist and every lookup misses, leaving the key itself in the answer. And with both modules
        // copying to one `translations` folder, the two `en-US.json` overwrite each other in this output —
        // the host had Billing's file and no Registry message resolved. Hence a folder per module.
        i18n.AddJsonTranslations(Path.Combine(AppContext.BaseDirectory, "translations", "registry"));
        i18n.AddJsonTranslations(Path.Combine(AppContext.BaseDirectory, "translations", "billing"));
        // The refusals too: title and detail from the error's MessageKey, with {name} filled from its
        // Parameters.
        i18n.LocalizeProblemDetails();
    });

    // ⚠️ This call is also what puts an amount on the wire: `AddPragmaticInternationalization` — which
    // UseI18N calls, and which the generated host calls for a module that has translations — installs
    // MoneyJsonConverter and its siblings into the HTTP JSON options. Money travels as
    // { "amount": …, "currency": "EUR" } because of them, and no converter registration of its own is
    // needed here.
    //
    // An application with an amount on the wire and no translations at all gets the converters too:
    // the shape is a declaration of its own, and the generated host installs the converters alone
    // for it. This one is translated, so it gets the whole registration, of which they are
    // a part.


    // Where the issued documents are kept. Local disk is this example's deployment choice; the seam is
    // IFileStorage, and Azure Blob or S3 would be a different factory here and nothing else.
    app.UseStorage(services => new LocalDiskFileStorage(
        app.Configuration["Storage:Root"] ?? Path.Combine(AppContext.BaseDirectory, "storage"),
        services.GetRequiredService<ILogger<LocalDiskFileStorage>>()));

    // The nightly sweep that chases the overdue invoices. Two workers and a ten-second poll are this
    // deployment's numbers, not the framework's.
    //
    // The store is the database, not memory: a job enqueued and not yet run survives a restart, and the
    // lease means two hosts cannot both run the same occurrence. The tables are asked for once, by
    // [EnableJobPersistence] on BillingBoundary, because OnModelCreating is generated. ⚠️ UseEfCore() fails the start if no store is registered rather than
    // falling back to memory, which is why the next line is not optional.
    app.UseJobs(jobs =>
    {
        jobs.WithWorkerCount(2);
        jobs.WithPollingInterval(10);
        jobs.UseEfCore();
        jobs.UseEfCorePersistence();
    });

    // How the reminders leave the building. No DefaultFrom on purpose: every message names its sender, and
    // the sender is the company's own address — a default here would send every company's mail from one.
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

    // The schema follows the entities: the runner diffs the desired schema against the live database and
    // applies it at startup.
    app.UsePragmaticMigrations();

    // The API contract as the generator wrote it at compile time — the errors each operation declares and
    // the permission each one requires — in every environment. In Development the generated host publishes
    // it on its own, with Scalar over it.
    app.UseApiDocumentation();

    // Who the callers are: each company's own OpenID Connect provider. Invoicing issues no token and holds
    // no password — the provider signs, and the roles inside the token are the whole answer to what a caller
    // may do. Audience is mandatory outside Development, and the call throws at startup without it: leaving
    // it empty would disable audience validation and accept tokens minted for any other client of the same
    // provider.
    app.UseOidcAuthentication(oidc =>
    {
        oidc.Authority = app.Configuration["Oidc:Authority"] ?? "";
        oidc.Audience = app.Configuration["Oidc:Audience"];
        oidc.RoleClaim = "roles";
        oidc.NameClaim = "name";
        // The provider's metadata is fetched over HTTPS everywhere but on a developer's machine.
        oidc.RequireHttpsMetadata = !app.Environment.IsDevelopment();
    });

    // The tenant comes from the token — the `tenant_id` claim — and from nowhere else. A header, a route
    // segment or a subdomain is input the client controls, and an authenticated caller supplying another
    // company's id is exactly the escalation the claim guard exists to close. The four guards are written
    // out instead of left to their defaults, because this file is where one would look for them.
    app.UseMultiTenancy(tenancy =>
    {
        tenancy.UseClaim();
        tenancy.Services.Configure<MultiTenancyOptions>(options =>
        {
            options.RequireTenant = true;
            options.EnforceTenantClaim = true;
            options.EnforceTenantState = true;
            // Off by default only because the framework's stock store is empty; here the store is the
            // Organizations table, so an id that names no company is a 404 and not a silent write.
            options.RequireKnownTenant = true;
        });
    });

    // Authorization: the token carries the role name, and each role's permissions are listed in its class.
    app.UseAuthorization(authz =>
    {
        authz.MapRole<ViewerRole>();
        authz.MapRole<AccountantRole>();
        authz.MapRole<PlatformAdministratorRole>();
    });
}).ConfigureAwait(false);
