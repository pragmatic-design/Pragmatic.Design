namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Controls the visibility of the generated boundary interface.
/// </summary>
public enum BoundaryVisibility
{
    /// <summary>
    ///     Generates both public and internal interfaces.
    ///     The boundary is callable from other modules and can be exposed via endpoints.
    /// </summary>
    Public,

    /// <summary>
    ///     Generates only the internal interface.
    ///     The boundary is only callable within the same assembly (intra-boundary calls).
    ///     Cannot be used with <c>[ExposeEndpoint]</c> or remote boundaries.
    /// </summary>
    Internal
}
