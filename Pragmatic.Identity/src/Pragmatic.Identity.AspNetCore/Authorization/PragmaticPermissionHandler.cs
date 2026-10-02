using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Pragmatic.Authorization;
using Pragmatic.Endpoints.Authorization;

namespace Pragmatic.Identity.Authorization;

/// <summary>
///     Handles <see cref="PragmaticPermissionRequirement" /> by delegating to <see cref="IPermissionChecker" />.
///     Supports configurable permission sources (claims, database, external policy server).
/// </summary>
public sealed partial class PragmaticPermissionHandler(
    IPermissionChecker permissionChecker,
    ILogger<PragmaticPermissionHandler> logger) : AuthorizationHandler<PragmaticPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PragmaticPermissionRequirement requirement)
    {
        if (context.User.Identity is not { IsAuthenticated: true })
        {
            LogUnauthenticated();
            return;
        }

        if (requirement.Mode is not (PermissionMode.All or PermissionMode.Any))
        {
            // Unknown mode is a configuration error — fail closed and surface it, never silently deny.
            LogUnknownMode(requirement.Mode);
            return;
        }

        var granted = requirement.Mode switch
        {
            PermissionMode.All => await permissionChecker
                .HasAllPermissionsAsync(requirement.Permissions)
                .ConfigureAwait(false),
            PermissionMode.Any => await permissionChecker
                .HasAnyPermissionAsync(requirement.Permissions)
                .ConfigureAwait(false),
            _ => false
        };

        if (granted)
        {
            LogPermissionGranted(requirement.Mode, requirement.Permissions);
            context.Succeed(requirement);
        }
        else
        {
            LogPermissionDenied(requirement.Mode, requirement.Permissions);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Permission check skipped: user not authenticated")]
    private partial void LogUnauthenticated();

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Permission requirement has an unsupported mode {Mode}; denying access (fail-closed)")]
    private partial void LogUnknownMode(PermissionMode mode);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Permission {Mode} granted for [{Permissions}]")]
    private partial void LogPermissionGranted(PermissionMode mode, string[] permissions);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Permission {Mode} denied for [{Permissions}]")]
    private partial void LogPermissionDenied(PermissionMode mode, string[] permissions);
}
