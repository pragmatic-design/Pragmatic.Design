namespace Pragmatic.Composition;

/// <summary>
///     Hook for modules that need to map custom endpoints on the <c>WebApplication</c>.
///     Implementations are resolved from DI after <c>MapAllEndpoints</c> and called
///     to add module-specific routes (e.g. SignalR hubs, gRPC services).
/// </summary>
public interface IEndpointRouteBuilderConfigurator
{
    /// <summary>
    ///     Configures endpoints on the application.
    ///     The <paramref name="app"/> is the built <c>WebApplication</c>.
    ///     Typed as <c>object</c> to avoid an ASP.NET Core dependency in Abstractions — cast to
    ///     <c>IEndpointRouteBuilder</c> in your implementation.
    /// </summary>
    void Configure(object app);
}
