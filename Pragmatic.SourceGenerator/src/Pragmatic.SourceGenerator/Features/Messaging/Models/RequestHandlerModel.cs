using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>
///     Model for a class decorated with [RequestHandler] that implements
///     <c>IRequestHandler&lt;TRequest, TResponse&gt;</c>.
/// </summary>
internal sealed record RequestHandlerModel : GeneratorModel
{
    /// <summary>FQN of the request type (TRequest).</summary>
    public required string RequestTypeFqn { get; init; }

    /// <summary>FQN of the response type (TResponse).</summary>
    public required string ResponseTypeFqn { get; init; }

    /// <summary>Diagnostic location, value-equatable for incremental caching.</summary>
    public Pragmatic.SourceGenerator.Core.LocationInfo? LocationInfo { get; init; }

    /// <summary>The diagnostic location reconstructed from <see cref="LocationInfo"/>.</summary>
    public Microsoft.CodeAnalysis.Location? Location => LocationInfo?.ToLocation();
}
