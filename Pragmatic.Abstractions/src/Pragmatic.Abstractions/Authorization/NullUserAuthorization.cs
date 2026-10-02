namespace Pragmatic.Authorization;

/// <summary>
///     No-op <see cref="IUserAuthorization"/> for anonymous or unauthenticated users.
///     All checks return <c>false</c>, all collections are empty.
/// </summary>
public sealed class NullUserAuthorization : ConstantUserAuthorization
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullUserAuthorization Instance = new();

    private NullUserAuthorization() { }

    /// <inheritdoc />
    protected override bool DefaultResult => false;
}
