using Pragmatic.Authorization;
using Pragmatic.Composition.Hosting;
using Pragmatic.Identity;
using Pragmatic.Migrations.Extensions;

// The smallest host that holds up a full HTTP round trip: migrations to create the schema, and the
// development identity — the same handler that asks for nothing, plus the X-User-* headers for the only
// case that needs an authenticated caller (TheDerivedPermissionOnARead). Without headers, nothing
// changes: the conformance operations stay anonymous because authorization is a cell of its own in the
// matrix.
await PragmaticApp.RunAsync(args, app =>
{
    app.UsePragmaticMigrations();

    app.UseDevelopmentIdentity();

    // The minimum for a permission to be decided: the authorization services, and trust in the
    // "permission" claims the X-User-Permissions headers produce. ⚠️ Without this line the development
    // identity is registered and the first route with a requirement answers 500 — the permission
    // checker asks for an IUserAuthorization nobody registered — instead of saying what is missing.
    app.UseAuthorization(authz => authz.TrustPermissionClaims = true);
}).ConfigureAwait(false);
