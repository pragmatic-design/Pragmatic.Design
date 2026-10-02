using Pragmatic.Agent.Client;
using Pragmatic.Authorization;
using Warehouse.Stock.Infrastructure.Authorization;
using Pragmatic.Caching.Redis;
using Pragmatic.Composition.Hosting;
using Pragmatic.Endpoints.OpenApi;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Internationalization;
using Pragmatic.Internationalization.Types;
using Pragmatic.Jobs;
using Pragmatic.Jobs.EFCore.Extensions;
using Pragmatic.Messaging.Batch;
using Pragmatic.Messaging.Extensions;
using Warehouse.Stock.Imports.Messages;
using Warehouse.Stock.Infrastructure.Imports;
using Pragmatic.Messaging.RabbitMQ;
using Pragmatic.Migrations.Extensions;

await PragmaticApp.RunAsync(args, app =>
{
    app.UseI18N(i18n =>
    {
        i18n.DefaultCulture(CultureCode.EnglishUS);
        i18n.Support(CultureCode.EnglishUS, CultureCode.FromString("it-IT"));
    });

    // Where Orders' reservation requests arrive. Both instances consume the same request queue, so each
    // request is answered once, by whichever instance takes it.
    //
    // And what it publishes: an expired hold, from the outbox the boundary declares. The registry of the
    // contracts assembly, where the event is declared, is what lets the pump read its own rows (PRAG1699).
    global::Warehouse.Stock.Contracts.Generated.PragmaticMessageHandlerRegistration
        .AddPragmaticMessageHandlers(app.Services);

    app.UseMessaging(msg =>
    {
        msg.UseRabbitMq(rabbit =>
        {
            rabbit.ConnectionString = app.Configuration["Messaging:RabbitMq:ConnectionString"]
                ?? throw new InvalidOperationException("Messaging:RabbitMq:ConnectionString is required.");
        });

        // How soon an expired hold reaches Orders after it is given back.
        msg.EnableOutbox(outbox =>
            outbox.PollingIntervalSeconds = app.Configuration.GetValue("Messaging:OutboxPollingIntervalSeconds", 5));

        // A supplier's file is dispatched as a batch of parts; each part is counted as it is handled, in
        // the progress table the boundary declares ([EnableBatchProgress]).
        msg.EnableBatchProcessing();
    });

    // How an import is cut into parts: already cut, where the file was read (StartImportAction).
    app.Services.AddSingleton<IBatchSplitter<StockImport, ImportFilePart>, StockImportSplitter>();

    // The workers that give back an expired hold. Both instances run them against the same rows; the
    // store's lease is what stops two of them running the same job. The poll is how late a hold can be
    // given back, so it is configuration, not a constant.
    app.UseJobs(jobs =>
    {
        jobs.WithPollingInterval(app.Configuration.GetValue("Jobs:PollingIntervalSeconds", 10));
        jobs.UseEfCore();
        jobs.UseEfCorePersistence();
    });

    // Two instances, one truth about availability. Each keeps its own in-process copy of the [Cacheable]
    // read, and a movement invalidates the copy of the instance that made it — so without this the other
    // instance answers what it cached before, until the entry expires. The broadcast carries every
    // invalidation to every instance over Redis. Measured: without it, a receipt on one instance is not
    // what the other reads next (TwoInstancesAgreeOnAvailability).
    //
    // ⚠️ It shares the invalidations, not the values: each instance still reads the database for its
    // own copy after a drop.
    var redis = app.Configuration.GetConnectionString("Redis");
    if (!string.IsNullOrWhiteSpace(redis))
        app.Services.AddRedisCacheInvalidationBroadcast(redis);

    app.UsePragmaticMigrations();

    app.UseApiDocumentation();

    // A token signed with Jwt:Key, holding no accounts: `Jwt:RequireSecurityStamp` is false because there
    // is no account store whose stamp could revoke it.
    app.UseJwtAuthentication();

    // What each role may do: the clerk receives goods, the manager also runs the catalogue and corrects
    // a level. A role is a claim in the token; the permissions it stands for are decided here.
    app.UseAuthorization(authz =>
    {
        authz.MapRole<StockClerkRole>();
        authz.MapRole<StockManagerRole>();
    });

    // The Agent on this machine, at Pragmatic:Agent:SocketPath. Unreachable, the host keeps serving:
    // the Agent coordinates the hosts, it does not stand between a host and its callers.
    app.UseAgent();
}).ConfigureAwait(false);
