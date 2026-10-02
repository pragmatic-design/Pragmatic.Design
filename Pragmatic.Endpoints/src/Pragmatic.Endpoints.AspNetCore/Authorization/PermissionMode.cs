namespace Pragmatic.Endpoints.Authorization;

/// <summary>
///     Determines how multiple permissions are evaluated.
/// </summary>
public enum PermissionMode
{
    /// <summary>All listed permissions are required.</summary>
    All,

    /// <summary>At least one of the listed permissions is required.</summary>
    Any
}
