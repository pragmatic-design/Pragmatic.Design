namespace Pragmatic.Abstractions.Http;

/// <summary>
///     Declares how callers authenticate, for the published contract to say so.
/// </summary>
/// <remarks>
///     <para>
///         Registered by whoever sets the authentication up — <c>UseJwtAuthentication</c>,
///         <c>UseKeycloakAuthentication</c>, an application with a handler of its own. The document
///         collects what it finds instead of consulting a list, so a way to authenticate that does
///         not exist yet needs no change here: it arrives carrying its own description.
///     </para>
///     <para>
///         ⚠️ Registering one does <b>not</b> apply it to any operation. Which operations require
///         authentication is settled at compile time from <c>[AllowAnonymous]</c>, and this only says
///         what the requirement looks like on the wire. Two schemes registered means two published,
///         and an operation that is not anonymous requires all of them.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public sealed class ApiKeyScheme : IOpenApiSecuritySchemeContributor
/// {
///     public OpenApiSecurityScheme Describe() => new()
///     {
///         Name = "apiKey", Type = "apiKey", ParameterName = "X-Api-Key", In = "header",
///     };
/// }
///
/// services.AddSingleton&lt;IOpenApiSecuritySchemeContributor, ApiKeyScheme&gt;();
///     </code>
/// </example>
public interface IOpenApiSecuritySchemeContributor
{
    /// <summary>The scheme this authentication publishes.</summary>
    OpenApiSecurityScheme Describe();
}
