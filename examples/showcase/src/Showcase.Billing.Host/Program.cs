using Pragmatic.Composition.Hosting;
using Pragmatic.Documents.Markup;
using Pragmatic.Internationalization;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
await PragmaticApp.RunAsync(args, app =>
{
    // Database — create tables automatically in development
    if (app.Environment.IsDevelopment())
        app.UseDatabaseEnsureCreated();

    // Authentication — development identity from X-User-* headers.
    //
    // ⚠️ Was a bare UseAuthentication<NoOpAuthenticationHandler>(…), which answers 401 to everything:
    // that handler reads no credential of its own, it honours whatever HeaderUserMiddleware put on the
    // context, and nothing here was adding that middleware. The same copy-without-the-other-half as
    // Showcase.Host.Distributed, found the same way — by finally starting the host.
    app.UseDevelopmentIdentity();

    // Authorization.
    //
    // ⚠️ Not only about permissions: the call
    // registers IUserScopeResolver, which the SG-generated [HasAccessScopes] data filters take from DI
    // — Invoice has one — so without it every scoped query on this host is an unresolvable service.
    app.UseAuthorization(authz =>
    {
        // As in the other two hosts: this demo carries permissions in X-User-Permissions.
        authz.TrustPermissionClaims = true;
        authz.UsePermissionCache(TimeSpan.FromMinutes(5));
    });

    // Logging — Console only for standalone host
    app.UseLogging(log =>
    {
        log.AddConsole(PragmaticConsoleConfiguration.ForDevelopment());
    });
}).ConfigureAwait(false);

/// <summary>
///     Named so a test can boot this host. Top-level statements compile to an <c>internal</c>
///     <c>Program</c>, which <c>WebApplicationFactory&lt;TEntryPoint&gt;</c> elsewhere cannot name.
/// </summary>
public partial class Program;
