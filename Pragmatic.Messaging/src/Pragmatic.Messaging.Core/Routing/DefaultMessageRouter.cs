namespace Pragmatic.Messaging.Routing;

/// <summary>
///     Convention-based message router. Used as fallback when SG-generated router is not available.
///     Convention: topic = {boundary-kebab}.events, send queue = {boundary-kebab}.commands.{message-kebab}.
/// </summary>
/// <remarks>
///     A <b>subscription</b> name is not here: it needs the subscriber, which a router that only sees
///     types cannot know. See <see cref="SubscriptionName" />.
/// </remarks>
public sealed class DefaultMessageRouter : IMessageRouter
{
    public string GetTopic<T>() where T : notnull => GetTopic(typeof(T));

    public string GetTopic(Type messageType)
    {
        var ns = messageType.Namespace ?? "";
        var boundary = ExtractBoundary(ns);
        return $"{boundary}.events";
    }

    public string GetSendQueue(Type messageType)
    {
        // Point-to-point queue derived from the message's boundary.
        // Convention: "{boundary}.commands.{message-kebab}".
        var ns = messageType.Namespace ?? "";
        var boundary = ExtractBoundary(ns);
        var msgName = ToKebabCase(messageType.Name);
        return $"{boundary}.commands.{msgName}";
    }

    private static string ExtractBoundary(string ns)
    {
        // Extract boundary from namespace: "Showcase.Billing.EventHandlers" → "billing"
        var parts = ns.Split('.');
        return parts.Length >= 2
            ? ToKebabCase(parts[1])
            : ToKebabCase(parts[0]);
    }

    private static string ToKebabCase(string name) => MessagingNames.ToKebabCase(name);
}
