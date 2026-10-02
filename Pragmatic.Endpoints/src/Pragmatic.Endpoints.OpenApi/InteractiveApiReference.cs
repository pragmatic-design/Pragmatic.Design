using Microsoft.AspNetCore.Routing;

namespace Pragmatic.Endpoints.OpenApi;

/// <summary>
///     A browsable reference over the published document — Scalar, when the host references it.
/// </summary>
/// <param name="PathPrefix">
///     The prefix every route of the reference lives under: a route already mapped under it means the
///     application mapped the reference itself.
/// </param>
/// <param name="Map">Maps the reference's routes.</param>
/// <remarks>
///     Described by the generated host rather than known here: this package does not reference the
///     reference's package, and the host is the only compilation that can say whether it is there.
/// </remarks>
public sealed record InteractiveApiReference(string PathPrefix, Action<IEndpointRouteBuilder> Map);
