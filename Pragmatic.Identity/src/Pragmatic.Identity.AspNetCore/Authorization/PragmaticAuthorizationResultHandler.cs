using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.Binding;
using Pragmatic.Endpoints.Authorization;
using Pragmatic.Endpoints.Extensions;
using Pragmatic.Result;
using Pragmatic.Result.Http;

namespace Pragmatic.Identity.Authorization;

/// <summary>
///     Answers a denied authorization with a ProblemDetails naming the permission that was missing.
/// </summary>
/// <remarks>
///     <para>
///         ASP.NET's default result handler writes a bare 403 with an empty body. Every consumer that
///         had to tell a caller *why* they were refused wrote this class themselves — two independent
///         projects did, which is the signal that it belongs in the framework rather than in each
///         application.
///     </para>
///     <para>
///         The body is the <see cref="ForbiddenError" /> written by the path that writes every other error
///         (<c>ToProblemDetails</c> with the host's <see cref="IErrorMessageResolver" />): a refusal from the
///         gate and one returned from the action pipeline have the same shape — <c>requiredPermissions</c>
///         and <c>permissionMatch</c> — and speak the same language, rather than a key and an English
///         sentence of this handler's own. Only the failed
///         <see cref="PragmaticPermissionRequirement" /> is named: a policy failure with no permission
///         attached stays generic rather than inventing a reason.
///     </para>
/// </remarks>
public sealed class PragmaticAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <inheritdoc />
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        // Challenged is the authentication stack's business (401 + scheme headers); only a refusal
        // of an authenticated caller is ours to explain.
        if (!authorizeResult.Forbidden || context.Response.HasStarted)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
            return;
        }

        var failed = authorizeResult.AuthorizationFailure?.FailedRequirements
            .OfType<PragmaticPermissionRequirement>()
            .ToArray() ?? [];

        var error = RefusalFor(failed);
        var resolver = context.RequestServices?.GetService<IErrorMessageResolver>();
        var problem = error.ToProblemDetails(resolver);
        problem.Instance = context.Request.Path;

        context.Response.StatusCode = error.StatusCode;
        await context.Response
            .WriteAsJsonAsync(problem, ProblemDetailsJson.TypeInfo(context), contentType: "application/problem+json")
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     The refusal the failed requirements add up to.
    /// </summary>
    /// <remarks>
    ///     Requirements of a policy are all required. Several that each need all their permissions add up
    ///     to all of those permissions. A requirement satisfied by any of its permissions does not flatten
    ///     into a list — "any of a, b" and "any of c, d" is not "any of a, b, c, d" — so with more than one
    ///     requirement and an any among them, the first failed requirement is named as it is: true, if not
    ///     the whole story.
    /// </remarks>
    private static ForbiddenError RefusalFor(PragmaticPermissionRequirement[] failed)
    {
        if (failed.Length == 0)
            return new ForbiddenError();

        if (failed.Length == 1 || failed.All(r => r.Mode == PermissionMode.All))
        {
            var first = failed[0];
            var permissions = failed.SelectMany(r => r.Permissions).Distinct().ToArray();
            var match = failed.Length == 1 && first.Mode == PermissionMode.Any ? PermissionMatch.Any : PermissionMatch.All;
            return ForbiddenError.MissingPermissions(permissions, match);
        }

        var named = failed[0];
        return ForbiddenError.MissingPermissions(
            named.Permissions, named.Mode == PermissionMode.Any ? PermissionMatch.Any : PermissionMatch.All);
    }
}
