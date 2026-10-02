namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The name each <c>HttpVerb</c> value carries in an endpoint model.
/// </summary>
/// <remarks>
///     One place, because there are now two readers of the same attribute: the endpoint transform, for a
///     class, and the specification-query transform, for the <c>[Endpoint]</c> written beside a rule. The
///     names are what <c>MapInvocationHelper</c> switches on and what the manifest publishes, so two
///     copies of this map would let a route and its published verb disagree.
/// </remarks>
internal static class HttpVerbNames
{
    /// <summary>The verb's name, defaulting to <c>Get</c> for a value the enum does not define.</summary>
    public static string FromValue(int value)
        => value switch
        {
            0 => "Get",
            1 => "Post",
            2 => "Put",
            3 => "Patch",
            4 => "Delete",
            5 => "Head",
            6 => "Options",
            _ => "Get"
        };
}
