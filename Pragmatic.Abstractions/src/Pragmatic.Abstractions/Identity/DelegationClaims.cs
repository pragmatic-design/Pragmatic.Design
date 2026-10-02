namespace Pragmatic.Identity;

/// <summary>
///     The claims that carry a delegation on a token.
/// </summary>
/// <remarks>
///     <c>act_sub</c> is RFC 8693 (OAuth 2.0 Token Exchange), where the <c>act</c> claim names the
///     acting party beside the subject. The others are ours, prefixed to sit beside it without
///     pretending to be standard.
/// </remarks>
public static class DelegationClaims
{
    /// <summary>RFC 8693: the acting party's subject.</summary>
    public const string ActorSubject = "act_sub";

    /// <summary>The actor's kind — see <see cref="ActorKind" />.</summary>
    public const string ActorKind = "act_kind";

    /// <summary>How authority composes — see <see cref="DelegationPolicy" />.</summary>
    public const string Policy = "act_policy";

    /// <summary>Why the delegation exists, for the audit trail.</summary>
    public const string Purpose = "act_purpose";

    /// <summary>The grant that authorised it.</summary>
    public const string GrantId = "act_grant";

    /// <summary>The acting parties from the outermost inwards, when there is more than one.</summary>
    public const string Chain = "act_chain";

    /// <summary>The actor's own permissions, which <c>Intersection</c> narrows the subject's against.</summary>
    /// <remarks>
    ///     Here rather than beside the resolver that reads it, because the development identity has to
    ///     emit the same name and lives in another assembly. A claim name written in two places is the
    ///     drift this repository has already paid for more than once.
    /// </remarks>
    public const string ActorPermissions = "act_perms";

    /// <summary>The permissions the grant names, for <c>GrantScoped</c>.</summary>
    public const string GrantPermissions = "act_grant_perms";
}
