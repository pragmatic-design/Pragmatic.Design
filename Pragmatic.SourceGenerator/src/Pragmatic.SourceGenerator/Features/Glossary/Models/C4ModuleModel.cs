namespace Pragmatic.SourceGenerator.Features.Glossary.Models;

/// <summary>
///     One module/container in the generated C4 container diagram: a module composed into the host via
///     <c>[Include&lt;TModule&gt;]</c> (in-process) or <c>[RemoteBoundary&lt;TModule&gt;]</c> (remote).
/// </summary>
internal sealed record C4ModuleModel
{
    /// <summary>Module type name.</summary>
    public required string Name { get; init; }

    /// <summary>True when composed via [RemoteBoundary] (a separate process), false for in-process [Include].</summary>
    public bool IsRemote { get; init; }
}
