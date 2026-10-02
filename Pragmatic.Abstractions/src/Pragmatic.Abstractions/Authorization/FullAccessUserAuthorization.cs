namespace Pragmatic.Authorization;

/// <summary>
///     Full-access <see cref="IUserAuthorization"/> for system-level contexts.
///     All permission checks return <c>true</c>.
///     <para>
///         <b>Note:</b> collection properties (<c>Roles</c>, <c>Permissions</c>, etc.)
///         are empty because the actual role/permission set is unknown. Use <c>HasPermission</c>,
///         <c>IsInRole</c>, etc. for authorization checks — do not enumerate the collections.
///     </para>
/// </summary>
public sealed class FullAccessUserAuthorization : ConstantUserAuthorization
{
    /// <summary>
    ///     Singleton instance.
    ///     <para>
    ///         <b>System use only.</b> This instance grants all permissions unconditionally.
    ///         Inject it only in trusted system contexts (background jobs, migrations, internal
    ///         services running outside a user request). Never inject it into user-facing code
    ///         or as a default fallback — doing so silently escalates privileges for all callers
    ///         that rely on permission checks.
    ///     </para>
    /// </summary>
    public static readonly FullAccessUserAuthorization Instance = new();

    private FullAccessUserAuthorization() { }

    /// <inheritdoc />
    protected override bool DefaultResult => true;
}
