using Pragmatic.Agent.Client;
using Pragmatic.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Internationalization;
using Pragmatic.Internationalization.Types;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Migrations.Extensions;
using Warehouse.Shipping.Infrastructure.Authorization;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS);
        i18n.Support(CultureCode.EnglishUS, CultureCode.FromString("it-IT"));
    });

    // What this service publishes — a dispatch — from the outbox the boundary declares. The registry of the
    // contracts assembly, where the event is declared, is what lets the pump read its own rows (PRAG1699).
    global::Warehouse.Shipping.Contracts.Generated.PragmaticMessageHandlerRegistration
        .AddPragmaticMessageHandlers(app.Services);

    // Where picked orders arrive, and where dispatches leave for.
    app.UseMessaging(msg =>
    {
        msg.UseRabbitMq(rabbit =>
        {
            rabbit.ConnectionString = app.Configuration["Messaging:RabbitMq:ConnectionString"]
                ?? throw new InvalidOperationException("Messaging:RabbitMq:ConnectionString is required.");
        });

        // How soon a dispatch reaches the others after it is saved.
        msg.EnableOutbox(outbox =>
            outbox.PollingIntervalSeconds = app.Configuration.GetValue("Messaging:OutboxPollingIntervalSeconds", 5));
    });

    app.UsePragmaticMigrations();

    app.UseApiDocumentation();

    // A token signed with Jwt:Key, holding no accounts: `Jwt:RequireSecurityStamp` is false because there
    // is no account store whose stamp could revoke it.
    app.UseJwtAuthentication();

    // What the shipping clerk may do. A role is a claim in the token; the permissions it stands for are
    // decided here.
    app.UseAuthorization(authz => authz.MapRole<ShippingClerkRole>());

    // The Agent on this machine, at Pragmatic:Agent:SocketPath. Unreachable, the host keeps serving:
    // the Agent coordinates the hosts, it does not stand between a host and its callers.
    app.UseAgent();
}).ConfigureAwait(false);
