using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds the authorization middleware, after the tenant has been resolved and the culture is known.
/// </summary>
/// <remarks>
///     <para>
///         Order 94 — after <c>TenantResolutionStep</c> (92), and that is the whole reason this is a
///         step of its own rather than the second line of <see cref="AuthenticationStep" />.
///         Authentication has to run <em>before</em> tenant resolution, because the tenant middleware
///         checks the resolved tenant against the tenant claim on the authenticated principal.
///         Authorization has to run <em>after</em> it, because a permission can depend on which
///         tenant is being asked about.
///     </para>
///     <para>
///         ⚠️ Adding both middleware together, before tenant resolution, makes an
///         <c>IPermissionProvider</c> that reads tenant-scoped data impossible: the handler runs while
///         <c>ITenantContext.TenantId</c> is still null, so the provider reads nothing, and
///         <c>CachedPermissionResolver</c> caches that empty answer for the rest of the request. The
///         symptom is a 403 on an endpoint whose permission has just been granted, with the correct
///         row sitting in the database and nothing to point at.
///     </para>
///     <para>
///         ⚠️ It also runs after <c>InternationalizationStep</c> (93). Ahead of it, the refusal this
///         middleware writes would be the <b>only error in the application with no language</b>: every
///         other one is written by the endpoint, at the end of the pipeline. The 403's title and detail
///         would fall back to <c>CultureInfo.CurrentCulture</c> — the machine's locale — so the same
///         request would answer in Italian on a developer's machine and in English on the runner, and a
///         test asserting the translated refusal would pass for that reason alone.
///     </para>
/// </remarks>
public sealed class AuthorizationStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 94;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        if (app.ApplicationServices.GetService<IAuthenticationSchemeProvider>() is null)
            return;

        // UseAuthorization needs the policy services; AddAuthentication alone does not add them.
        if (app.ApplicationServices.GetService<IAuthorizationPolicyProvider>() is not null)
            app.UseAuthorization();
    }
}
