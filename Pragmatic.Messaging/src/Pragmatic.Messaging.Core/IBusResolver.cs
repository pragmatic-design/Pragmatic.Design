namespace Pragmatic.Messaging;

/// <summary>
///     Resolves the target bus name for a handler type at runtime.
///     Implementation is SG-generated via <c>PragmaticBusResolver</c>
///     from <c>[OnBus("name")]</c> attributes.
/// </summary>
public interface IBusResolver
{
    /// <summary>
    ///     Returns the bus name for the given handler type FQN.
    ///     Returns <c>null</c> for the default bus.
    /// </summary>
    string? GetBusName(string handlerTypeFqn);
}

/// <summary>
///     Default implementation that routes all handlers to the default bus.
/// </summary>
public sealed class DefaultBusResolver : IBusResolver
{
    public static readonly DefaultBusResolver Instance = new();
    public string? GetBusName(string handlerTypeFqn) => null;
}
