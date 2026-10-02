using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition;

namespace Pragmatic.Abstractions.Http;

/// <summary>
///     Declares, for the published contract, how callers authenticate.
/// </summary>
public static class PragmaticBuilderOpenApiExtensions
{
    /// <summary>
    ///     Adds a security scheme to the published OpenAPI document.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Call it where the authentication is configured. The three JWT entry points call it for
    ///         you, and <c>UseDevelopmentIdentity</c> calls it with the header the development
    ///         middleware reads — an application only needs this when it installs an authentication
    ///         of its own.
    ///     </para>
    ///     <para>
    ///         ⚠️ A method rather than one more parameter on <c>UseAuthentication</c>, because an
    ///         application can have <b>more than one</b> way in: a bearer token for people and an API
    ///         key for machines is an ordinary arrangement, and a parameter would have made the
    ///         second one impossible to express. Call it once per scheme.
    ///     </para>
    ///     <para>
    ///         ⚠️ This does <b>not</b> decide which operations require authentication — that is
    ///         <c>[AllowAnonymous]</c>, settled at compile time. It says what the requirement looks
    ///         like on the wire. Registering two schemes means an operation that is not anonymous
    ///         requires both.
    ///     </para>
    /// </remarks>
    /// <param name="builder">The builder.</param>
    /// <param name="scheme">The scheme, as the contract should describe it.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IPragmaticBuilder DescribeSecurityScheme(
        this IPragmaticBuilder builder, OpenApiSecurityScheme scheme)
    {
        builder.Services.AddSingleton<IOpenApiSecuritySchemeContributor>(
            _ => new StaticSecuritySchemeContributor(scheme));

        return builder;
    }

    /// <summary>Carries one already-decided scheme; the interface exists for the ones that compute it.</summary>
    private sealed class StaticSecuritySchemeContributor(OpenApiSecurityScheme scheme)
        : IOpenApiSecuritySchemeContributor
    {
        public OpenApiSecurityScheme Describe() => scheme;
    }
}
