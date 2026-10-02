namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Flags enum specifying which CRUD operations the source generator should
///     auto-scaffold for a <see cref="ResourceAttribute"/>.
/// </summary>
[Flags]
public enum ResourceCapabilities
{
    /// <summary>No auto-generated CRUD endpoints.</summary>
    None = 0,

    /// <summary>POST — create a new resource.</summary>
    Create = 1,

    /// <summary>GET by id — read a single resource.</summary>
    Read = 2,

    /// <summary>PUT/PATCH — update an existing resource.</summary>
    Update = 4,

    /// <summary>DELETE — remove a resource.</summary>
    Delete = 8,

    /// <summary>GET collection — list resources with pagination.</summary>
    List = 16,

    /// <summary>GET with query — search/filter resources.</summary>
    Search = 32,

    /// <summary>POST {id}/restore — put a soft-deleted resource back.</summary>
    /// <remarks>
    ///     Only meaningful for an entity with <c>[SoftDelete]</c>: there is nothing to restore once a
    ///     row is gone. Asking for it on an entity without soft delete is ignored rather than generated,
    ///     because the mutation it would scaffold cannot compile — see PRAG2606.
    /// </remarks>
    Restore = 64,

    /// <summary>
    ///     Everything that applies: Create, Read, Update, Delete, List, Search — and Restore when the
    ///     entity is soft-deletable.
    /// </summary>
    All = Create | Read | Update | Delete | List | Search | Restore,
}
