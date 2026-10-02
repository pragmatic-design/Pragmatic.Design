namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Binds a property value from a JWT claim in the authenticated user's identity.
/// </summary>
/// <remarks>
///     <para>
///         Since ASP.NET Core Minimal APIs don't support claim binding natively,
///         the source generator extracts the claim from <c>HttpContext.User</c> inside
///         the handler body. Non-string types are parsed automatically.
///     </para>
///     <para>
///         Supported types: <c>string</c>, <c>Guid</c>, <c>int</c>, <c>long</c>,
///         <c>bool</c>, <c>DateTimeOffset</c>.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Get, "/api/my-profile")]
/// public partial class GetMyProfile : Endpoint&lt;ProfileDto&gt;
/// {
///     [FromClaim("sub")]
///     public Guid UserId { get; set; }
///
///     [FromClaim("name")]
///     public string UserName { get; set; } = "";
///
///     [FromClaim("role", IsRequired = false)]
///     public string? Role { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromClaimAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of <see cref="FromClaimAttribute" /> with the specified claim type.
    /// </summary>
    /// <param name="claimType">The claim type to bind from (e.g., "sub", "name", "role").</param>
    public FromClaimAttribute(string claimType) => ClaimType = claimType;

    /// <summary>
    ///     Gets the claim type to bind from the user's identity.
    /// </summary>
    public string ClaimType { get; }

    /// <summary>
    ///     Gets or sets whether the claim is required. Defaults to <c>true</c>.
    ///     When required, the endpoint returns 401 if the claim is missing.
    /// </summary>
    public bool IsRequired { get; set; } = true;
}
