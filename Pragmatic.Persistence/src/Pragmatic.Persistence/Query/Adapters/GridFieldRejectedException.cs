namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Why a grid clause names a field the bridge will not filter on.
/// </summary>
public enum GridFieldRejection
{
    /// <summary>The entity has no such property, or it is not filterable.</summary>
    Unknown,

    /// <summary>
    ///     The property exists and is deliberately withheld from client-driven filtering.
    /// </summary>
    /// <remarks>
    ///     Credential columns and the authorization shape — <c>PasswordHash</c>, <c>OwnerId</c>,
    ///     <c>TenantId</c>, <c>AccessScopes</c>. A <c>startswith</c> against one of them turns the
    ///     presence or absence of rows into a boolean oracle, which reads the value one character at a
    ///     time.
    /// </remarks>
    Withheld,
}

/// <summary>
///     A grid asked to filter on a field the bridge will not filter on.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Neither case may be silence.</b> A generated switch with no <c>default</c> lets a
///         clause naming an unknown field fall through, and the query comes back unchanged — a filter
///         the client believes it applied returns <em>more</em> rows, with a success status. A denylist
///         working by the same omission makes the security control and a typo produce identical
///         responses: neither a client nor a log could tell "you may not filter on that" from "you
///         spelled it wrong".
///     </para>
///     <para>
///         Thrown rather than returned, because a caller that ignores a return value is exactly how
///         the silence comes back. <see cref="Rejection" /> lets a handler answer 400 for a typo and 403
///         — or 400 without detail — for a withheld field, which is a decision about disclosure and
///         belongs to the application, not here.
///     </para>
/// </remarks>
public sealed class GridFieldRejectedException : InvalidOperationException
{
    /// <summary>Creates the exception for a field and a reason.</summary>
    public GridFieldRejectedException(string field, GridFieldRejection rejection)
        : base(rejection == GridFieldRejection.Withheld
            ? $"The field '{field}' cannot be filtered on."
            : $"There is no filterable field named '{field}'.")
    {
        Field = field;
        Rejection = rejection;
    }

    /// <summary>The field name exactly as the grid spelled it.</summary>
    public string Field { get; }

    /// <summary>Whether the field is unknown or deliberately withheld.</summary>
    public GridFieldRejection Rejection { get; }
}
