using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Endpoints.Configuration;
using Pragmatic.Integration.Tests.Domain.Actions;

namespace Pragmatic.Integration.Tests.Infrastructure;

/// <summary>
///     The same pipeline as <see cref="IntegrationTestFactory" />, with an authentication and
///     authorization stack in front of the generated endpoints — and the endpoint options left to the
///     caller. <c>null</c> registers none, which is what a host that never configures them has.
/// </summary>
/// <param name="endpointOptions">The endpoint options to register, or <c>null</c> for none.</param>
/// <param name="authorization">Policies the host declares, for the endpoints to be checked against.</param>
/// <param name="publishOpenApi">
///     Whether the host publishes the runtime OpenAPI document at <c>/openapi/v1.json</c>, next to a
///     hand-written endpoint at <c>/hand-written</c> that belongs to the application, not to Pragmatic.
/// </param>
/// <param name="services">Whatever else the host registers, after everything above.</param>
/// <param name="outputCache">
///     Whether the host turns ASP.NET's output cache on — the step without which a shared
///     <c>[ResponseCache]</c> does nothing — after authentication, where an application puts it.
/// </param>
public sealed class AuthenticatingTestFactory(
    PragmaticEndpointsOptions? endpointOptions,
    Action<AuthorizationOptions>? authorization = null,
    bool publishOpenApi = false,
    Action<IServiceCollection>? services = null,
    bool outputCache = false) : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"AuthenticatingTest_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(registered =>
        {
            registered.AddDbContext<TestDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            Pragmatic.Actions.Extensions.ServiceCollectionExtensions.AddPragmaticActions(registered);
            PragmaticActionsRegistrationExtensions.AddPragmaticActions(registered);
            PragmaticEndpointsRegistrationExtensions.AddPragmaticEndpoints(registered);

            // What the actions registered above inject: the host validates every registration at build.
            registered.AddSingleton<IRateSource, CountingRateSource>();

            registered
                .AddAuthentication(NoCredentialsAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, NoCredentialsAuthenticationHandler>(
                    NoCredentialsAuthenticationHandler.SchemeName, configureOptions: null);
            registered.AddAuthorization(options => authorization?.Invoke(options));

            if (publishOpenApi)
                registered.AddOpenApi();

            if (endpointOptions is not null)
                registered.AddSingleton(endpointOptions);

            if (outputCache)
                registered.AddOutputCache();

            services?.Invoke(registered);
        });

        builder.Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            if (outputCache)
                app.UseOutputCache();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapPragmaticEndpoints();

                if (publishOpenApi)
                {
                    endpoints.MapOpenApi();
                    endpoints.MapGet("/hand-written", () => "the application's own").AllowAnonymous();
                }
            });
        });
    }
}
