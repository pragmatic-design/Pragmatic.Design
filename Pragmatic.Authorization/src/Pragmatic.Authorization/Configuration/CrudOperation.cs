namespace Pragmatic.Authorization.Configuration;

/// <summary>
///     Standard CRUD operations for permission assignment.
///     Used with <c>RoleBuilder.WithEntityPermissions</c> (Phase 5).
/// </summary>
public enum CrudOperation
{
    /// <summary>Read access.</summary>
    Read,

    /// <summary>Create access.</summary>
    Create,

    /// <summary>Update access.</summary>
    Update,

    /// <summary>Delete access.</summary>
    Delete
}
