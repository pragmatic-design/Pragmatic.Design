using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Local.Jwt;

namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     The signing parameters the suite gives Intake, so its tokens are validated as strictly in a test
///     as they are in production — and the minting of a caller's token.
/// </summary>
/// <remarks>
///     The key is a test's key and nothing else: it lives here, in the suite, and not in a committed
///     settings file of the application — <c>appsettings.Development.json</c> carries a development key
///     for a developer's own machine, which is a different decision with a different blast radius.
/// </remarks>
internal static class TestTokens
{
    internal const string SigningKey = "casework-integration-tests-signing-key-32-bytes-or-more";
    internal const string Issuer = "casework-intake";
    internal const string Audience = "casework-intake";

    /// <summary>
    ///     Verify's own issuer — and the audience too.
    /// </summary>
    /// <remarks>
    ///     A different one from Intake's on purpose: each service issues and validates its own tokens, so
    ///     Intake's token is rejected by Verify and the other way round. One shared issuer here would make
    ///     the suite pass on a pair of services that trust each other for no stated reason.
    /// </remarks>
    internal const string VerifyIssuer = "casework-verify";

    /// <summary>
    ///     Mints a token the running host will accept, signed by the host's own generator.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Through <see cref="JwtTokenGenerator" /> resolved from the host and not through a
    ///         hand-rolled <c>JwtSecurityToken</c> here: a second copy of the signing and claim-naming
    ///         rules in the suite would keep passing after the first copy changed, and the claim names
    ///         are exactly what the pipeline reads — <c>tenant_id</c> for the tenant, <c>role</c> for
    ///         the role.
    ///     </para>
    ///     <para>
    ///         No security stamp, and there is none to give: Casework keeps no local accounts, so there
    ///         is nothing whose rotation would revoke a token. That is the second case
    ///         <c>JwtOptions.RequireSecurityStamp</c> documents, and the host says so in configuration
    ///         rather than the suite relaxing it.
    ///     </para>
    /// </remarks>
    internal static string For(
        IServiceProvider host,
        string subject,
        string tenant,
        params string[] roles)
    {
        var generator = host.GetRequiredService<JwtTokenGenerator>();

        return generator.Generate(
            subject: subject,
            displayName: subject,
            tenantId: tenant,
            roles: roles).Token;
    }
}
