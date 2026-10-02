namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Allows anonymous access to an endpoint, overriding group-level authorization.
/// </summary>
/// <remarks>
///     <para>
///         Use this attribute when an endpoint in an authorized group should be
///         publicly accessible, such as login or registration endpoints.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [EndpointGroup("/api/auth")]
/// public sealed class AuthGroup { }
/// 
/// // This endpoint allows anonymous access even if the group requires auth
/// [Endpoint(HttpVerb.Post, "/login")]
/// [EndpointGroup&lt;AuthGroup&gt;]
/// [AllowAnonymous]
/// public partial class Login : Endpoint&lt;TokenResponse&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowAnonymousAttribute : Attribute
{
}