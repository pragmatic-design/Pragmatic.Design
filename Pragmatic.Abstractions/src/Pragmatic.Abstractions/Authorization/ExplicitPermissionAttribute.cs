namespace Pragmatic.Authorization;

/// <summary>
///     Overrides the auto-derived permission name for an action or mutation.
///     When applied, the SG uses the specified permission instead of deriving from namespace.
/// </summary>
/// <remarks>
///     Pass a generated constant — an entity's CRUD, or one declared with <see cref="PermissionAttribute" /> —
///     so the value stays in sync with the declaration rather than a free-form string. A constant the
///     generator does not write is <c>PRAG0421</c>, and the derived name is used instead.
/// </remarks>
/// <example>
///     <code>
/// [ExplicitPermission(BillingPermissions.Invoice.Refund)]
/// public partial class IssueRefundMutation : EntityMutation&lt;Invoice&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExplicitPermissionAttribute(string permission) : Attribute
{
    /// <summary>The explicit permission name to use.</summary>
    public string Permission { get; } = permission;
}
