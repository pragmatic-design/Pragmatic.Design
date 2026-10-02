using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Represents an [ExposeEndpoint&lt;TAction&gt;] declaration on a module class.
///     The host SG uses this to generate endpoint handler classes.
/// </summary>
internal sealed record ExposedEndpointModel
{
    /// <summary>FQN of the action type (with global:: prefix).</summary>
    public required string ActionTypeName { get; init; }

    /// <summary>Simple name of the action type (e.g., "LoginUser").</summary>
    public required string ActionSimpleName { get; init; }

    /// <summary>HTTP verb: "Get", "Post", "Put", "Patch", "Delete".</summary>
    public required string HttpVerb { get; init; }

    /// <summary>Route template relative to the package route prefix.</summary>
    public required string Route { get; init; }

    /// <summary>Optional endpoint name for link generation.</summary>
    public string? Name { get; init; }

    /// <summary>Optional group type FQN (from ExposeEndpoint&lt;T, TGroup&gt;).</summary>
    public string? GroupTypeName { get; init; }

    /// <summary>Additional permissions beyond the action's own [RequirePermission].</summary>
    public EquatableArray<string> AdditionalPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Permissions read from the action's own [RequirePermission] attributes.</summary>
    public EquatableArray<string> ActionPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>If true, bypasses all permission requirements.</summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>Assembly name where the action is defined (for package route prefix resolution).</summary>
    public required string ActionAssemblyName { get; init; }

    /// <summary>Boundary name where [ExposeEndpoint] is declared (for hint name grouping).</summary>
    public string? HostBoundaryName { get; init; }

    /// <summary>
    ///     The action's settable inputs, for a verb that carries no body.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A handler that read the action from the <b>request body</b> whatever the verb would make an
    ///     exposed GET answer <b>415</b> with the action never run — the route mapped, authorization
    ///     ran, and then the binding refused it. A GET carries its inputs on the query string, and
    ///     these are what read them. Empty for the verbs that do carry a body, which bind the whole
    ///     action from it.
    /// </remarks>
    public EquatableArray<ExposedInputModel> Inputs { get; init; } = EquatableArray<ExposedInputModel>.Empty;
}

/// <summary>One settable input of an exposed action, as the handler has to bind it.</summary>
/// <param name="Name">The property's name, which is also the query-string key.</param>
/// <param name="TypeName">Fully qualified, because the handler is emitted into the host's file.</param>
/// <param name="IsNullable">Whether the declaration accepts nothing.</param>
/// <param name="IsRequired">Whether the caller must supply it.</param>
internal sealed record ExposedInputModel(string Name, string TypeName, bool IsNullable, bool IsRequired);
