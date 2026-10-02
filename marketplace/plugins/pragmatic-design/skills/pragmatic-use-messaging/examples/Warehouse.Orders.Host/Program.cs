using Pragmatic.Agent.Client;
using Pragmatic.Authorization;
using Warehouse.Orders.Infrastructure.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Internationalization;
using Pragmatic.Internationalization.Types;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Configuration;
using Pragmatic.Messaging.Extensions;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Migrations.Extensions;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS);
        i18n.Support(CultureCode.EnglishUS, CultureCode.FromString("it-IT"));
    });

    // The broker placing an order asks Stock over, and how long the customer waits for the answer.
    // And what it publishes — a confirmed order — from the outbox the boundary declares. The registry of
    // the contracts assembly, where the event is declared, is what lets the pump read its own rows
    // (PRAG1699).
    global::Warehouse.Orders.Contracts.Generated.PragmaticMessageHandlerRegistration
        .AddPragmaticMessageHandlers(app.Services);

    app.UseMessaging(msg =>
    {
        msg.UseRabbitMq(rabbit =>
        {
            rabbit.ConnectionString = app.Configuration["Messaging:RabbitMq:ConnectionString"]
                ?? throw new InvalidOperationException("Messaging:RabbitMq:ConnectionString is required.");
        });

        // How soon a confirmation reaches Stock after it is saved.
        msg.EnableOutbox(outbox =>
            outbox.PollingIntervalSeconds = app.Configuration.GetValue("Messaging:OutboxPollingIntervalSeconds", 5));
    });

    // Not on the messaging builder, which has no setting for it: the bus reads the timeout from
    // IOptions<MessagingOptions>, and this is that.
    var timeout = app.Configuration.GetValue("Messaging:RequestReplyTimeout", TimeSpan.FromSeconds(5));
    app.Services.Configure<MessagingOptions>(options => options.RequestReplyTimeout = timeout);

    app.UsePragmaticMigrations();

    app.UseApiDocumentation();

    // A token signed with Jwt:Key, holding no accounts: `Jwt:RequireSecurityStamp` is false because there
    // is no account store whose stamp could revoke it.
    app.UseJwtAuthentication();

    // What the order desk may do. A role is a claim in the token; the permissions it stands for are
    // decided here.
    app.UseAuthorization(authz => authz.MapRole<OrderDeskRole>());

    // The Agent on this machine, at Pragmatic:Agent:SocketPath. Unreachable, the host keeps serving:
    // the Agent coordinates the hosts, it does not stand between a host and its callers.
    app.UseAgent();
}).ConfigureAwait(false);
