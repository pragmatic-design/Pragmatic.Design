namespace Pragmatic.Actions.Boundary;

/// <summary>
///     Specifies how a boundary is accessed at runtime.
/// </summary>
public enum BoundaryMode
{
    /// <summary>
    ///     The boundary runs in-process with direct database access.
    /// </summary>
    /// <remarks>
    ///     In this mode:
    ///     - Repositories use a DbContext to access the database
    ///     - DomainActions execute directly in the same process
    ///     - Transactions span the entire operation
    /// </remarks>
    Local,

    /// <summary>
    ///     The boundary is accessed via HTTP API calls.
    /// </summary>
    /// <remarks>
    ///     In this mode:
    ///     - Repositories are replaced with HTTP clients
    ///     - DomainActions are invoked via REST endpoints
    ///     - Each call is a separate HTTP request (no distributed transactions)
    /// </remarks>
    Remote
}
