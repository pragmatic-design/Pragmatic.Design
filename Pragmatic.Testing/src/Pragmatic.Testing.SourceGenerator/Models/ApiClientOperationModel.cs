using Pragmatic.SourceGen;

namespace Pragmatic.Testing.SourceGenerator.Models;

/// <summary>
///     One endpoint operation of the generated typed test client (Api.{Boundary}.{Name}Async):
///     correlation of an ApiRoutes builder method with its [PragmaticEndpointContract].
/// </summary>
internal sealed record ApiClientOperationModel
{
    public required string Boundary { get; init; }
    public required string Name { get; init; }
    public required string HttpMethod { get; init; }

    /// <summary>Fully qualified ApiRoutes builder invocation target (e.g. "global::App.ApiRoutes.Orders.GetOrder").</summary>
    public required string RouteBuilder { get; init; }

    /// <summary>Builder parameters, rendered as "type name" with optionality preserved.</summary>
    public EquatableArray<ApiClientParameterModel> Parameters { get; init; } = EquatableArray<ApiClientParameterModel>.Empty;

    /// <summary>Whether the endpoint accepts a JSON body (client takes an object payload).</summary>
    public bool HasBody { get; init; }

    /// <summary>Fully qualified response type, or null for void endpoints.</summary>
    public string? ResponseType { get; init; }
}

/// <summary>A single builder parameter of a client operation.</summary>
internal sealed record ApiClientParameterModel
{
    public required string TypeName { get; init; }
    public required string Name { get; init; }
    public bool IsOptional { get; init; }
}
