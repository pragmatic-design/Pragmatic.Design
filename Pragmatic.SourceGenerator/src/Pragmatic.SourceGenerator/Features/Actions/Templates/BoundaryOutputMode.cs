namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Determines which part of the boundary code to generate.
/// </summary>
internal enum BoundaryOutputMode
{
    /// <summary>Interfaces + DI extension method (the switch).</summary>
    Definition,

    /// <summary>Local implementation class + AddLocal method.</summary>
    Local,

    /// <summary>Remote HTTP implementation class + AddRemote method.</summary>
    Remote
}
