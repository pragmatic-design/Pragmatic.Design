using Microsoft.EntityFrameworkCore;
using Pragmatic.Composition;
using Pragmatic.Migrations.Extensions;
using Pragmatic.Notifications.Configuration;
using Pragmatic.Notifications.EFCore;
using Showcase.Accounts.Generated;

namespace Showcase.Host;

/// <summary>The host's wiring on EF Core. The Sql and documents hosts leave this file out and compile their own.</summary>
internal static partial class ShowcaseEngine
{
    /// <summary>Nothing to add: the generated host registers each boundary's DbContext from its database's connection string.</summary>
    public static partial void UsePersistence(IPragmaticBuilder app)
    {
    }

    /// <summary>
    ///     Pragmatic Migrations — declarative schema diff, replaces EF Core migrations entirely. Creates tables from scratch on an empty
    ///     database, applies the diff on an existing one.
    /// </summary>
    public static partial void UseSchema(IPragmaticBuilder app) => app.UsePragmaticMigrations();

    /// <summary>
    ///     What was sent, kept. ConnectionStrings:App is the key ShowcaseAppDatabase uses — the database the Booking boundary lives in,
    ///     which is the boundary that declares [StoresNotifications], so the table is created and migrated with the application's own.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without that declaration this call would wire a store whose table does not exist: a composed host creates one context per
    ///     declared database and nothing else.
    /// </remarks>
    public static partial void UseNotificationStore(NotificationsBuilder notifications, IPragmaticBuilder app)
        => notifications.UseEfCoreStore(db => db.UseNpgsql(
            app.Configuration.GetConnectionString("App")
            ?? throw new InvalidOperationException("ConnectionStrings:App is required.")));

    /// <summary>
    ///     The generated per-user culture provider, off unless asked for. It reads AppUser.PreferredCulture at priority 200 — above the
    ///     tenant default, below an explicit ?culture= on the request — and registers the AppUserResolver it reads through.
    /// </summary>
    public static partial void UseUserCulture(IPragmaticBuilder app) => app.UseUserCulture();
}
