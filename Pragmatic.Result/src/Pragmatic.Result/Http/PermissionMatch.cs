namespace Pragmatic.Result.Http;

/// <summary>
///     Whether a refusal needed every permission it names, or any one of them.
/// </summary>
public enum PermissionMatch
{
    /// <summary>Every permission is required.</summary>
    All,

    /// <summary>Any one of the permissions is enough.</summary>
    Any
}
