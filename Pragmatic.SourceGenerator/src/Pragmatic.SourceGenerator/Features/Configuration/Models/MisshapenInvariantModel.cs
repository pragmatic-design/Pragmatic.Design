using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Configuration.Models;

/// <summary>
///     A <c>[ConfigInvariant]</c> method whose shape the validator cannot call: static, with parameters,
///     or not returning <c>bool</c>. Carried so it is reported, not dropped.
/// </summary>
internal sealed record MisshapenInvariantModel
{
    public required string MethodName { get; init; }

    public LocationInfo? Location { get; init; }
}
