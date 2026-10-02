using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     Applies the trust-boundary authorization posture to the generated <c>/_pragmatic/invoke</c>
///     endpoint. Called by generated host code so the endpoint is never silently anonymous.
/// </summary>
public static class RemoteInvokeEndpointAuth
{
    /// <summary>The authorization posture resolved for the invoke endpoint.</summary>
    public enum Mode
    {
        /// <summary>Require an authenticated principal (default authorization policy).</summary>
        RequireAuthenticated,

        /// <summary>Require a named authorization policy.</summary>
        RequirePolicy,

        /// <summary>Explicitly allow anonymous access (trusted-network opt-out).</summary>
        Anonymous
    }

    /// <summary>
    ///     Reads <see cref="PragmaticRemoteInvokeOptions"/> from configuration and applies the
    ///     resulting authorization posture to <paramref name="builder"/>. Fail-closed by default.
    /// </summary>
    /// <param name="builder">The invoke endpoint convention builder.</param>
    /// <param name="services">The application service provider (supplies <see cref="IConfiguration"/>).</param>
    public static void Apply(IEndpointConventionBuilder builder, IServiceProvider services)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        Apply(builder, Resolve(services));
    }

    /// <summary>Resolves the invoke-endpoint options from configuration (defaults when absent).</summary>
    public static PragmaticRemoteInvokeOptions Resolve(IServiceProvider services)
    {
        var config = services.GetService<IConfiguration>();
        var section = PragmaticRemoteInvokeOptions.ConfigurationSection;

        return new PragmaticRemoteInvokeOptions
        {
            AllowAnonymous = string.Equals(
                config?[$"{section}:AllowAnonymous"], "true", StringComparison.OrdinalIgnoreCase),
            AuthorizationPolicy = config?[$"{section}:AuthorizationPolicy"]
        };
    }

    internal static void Apply(IEndpointConventionBuilder builder, PragmaticRemoteInvokeOptions options)
    {
        switch (Decide(options))
        {
            case Mode.Anonymous:
                builder.AllowAnonymous();
                break;
            case Mode.RequirePolicy:
                builder.RequireAuthorization(options.AuthorizationPolicy!);
                break;
            default:
                builder.RequireAuthorization();
                break;
        }
    }

    /// <summary>
    ///     Decides the posture: explicit anonymous wins; otherwise a configured policy; otherwise
    ///     the default authenticated-user requirement (fail-closed).
    /// </summary>
    public static Mode Decide(PragmaticRemoteInvokeOptions options)
        => options.AllowAnonymous
            ? Mode.Anonymous
            : !string.IsNullOrWhiteSpace(options.AuthorizationPolicy)
                ? Mode.RequirePolicy
                : Mode.RequireAuthenticated;
}
