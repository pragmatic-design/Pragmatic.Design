using Pragmatic.Actions.Boundary;

namespace Pragmatic.Actions.Samples.Boundaries;

/// <summary>
///     A local bounded context marker for the Orders domain.
///     Implements <see cref="IBoundary" /> so it can be configured via
///     <c>AddBoundary&lt;OrdersBoundary&gt;</c> and validated by the topology validator.
/// </summary>
/// <remarks>
///     In a real module this would carry the <c>[Boundary]</c> attribute as well, so the
///     source generator emits an <c>IOrdersActions</c> interface aggregating every action in
///     the <c>Orders.*</c> namespace. Here it is hand-written to keep the sample self-contained.
/// </remarks>
public sealed class OrdersBoundary : IBoundary;
