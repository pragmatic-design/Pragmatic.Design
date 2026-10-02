namespace Pragmatic.Authorization;

/// <summary>
///     Base class for <see cref="IUserAuthorization"/> implementations that return
///     a constant result for all authorization checks. Collections are always empty.
/// </summary>
/// <remarks>
///     Two concrete null-object subclasses are provided:
///     <list type="bullet">
///         <item>
///             <term><see cref="NullUserAuthorization"/></term>
///             <description>
///                 Denies all checks (<see cref="DefaultResult"/> = <c>false</c>).
///                 Use for anonymous or unauthenticated contexts.
///             </description>
///         </item>
///         <item>
///             <term><see cref="FullAccessUserAuthorization"/></term>
///             <description>
///                 Grants all checks (<see cref="DefaultResult"/> = <c>true</c>).
///                 Use only for trusted system/background contexts.
///             </description>
///         </item>
///     </list>
/// </remarks>
public abstract class ConstantUserAuthorization : IUserAuthorization
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();
    private static readonly IReadOnlyCollection<string> EmptyCollection = Array.Empty<string>();

    /// <summary>The constant result returned by all authorization checks.</summary>
    protected abstract bool DefaultResult { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => EmptyCollection;

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions => EmptySet;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Groups => EmptyCollection;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Scopes => EmptyCollection;

    /// <inheritdoc />
    public bool HasPermission(string permission) => DefaultResult;

    /// <inheritdoc />
    public bool HasAnyPermission(IEnumerable<string> permissions) => DefaultResult;

    /// <inheritdoc />
    public bool HasAllPermissions(IEnumerable<string> permissions) => DefaultResult;

    /// <inheritdoc />
    public bool IsInRole(string role) => DefaultResult;

    /// <inheritdoc />
    public bool IsInGroup(string group) => DefaultResult;

    /// <inheritdoc />
    public bool HasScope(string scope) => DefaultResult;
}
