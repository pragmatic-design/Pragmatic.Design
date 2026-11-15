namespace Pragmatic.Notifications.Tests.Unit;

/// <summary>One request a <see cref="ScriptedWebhookEndpoint" /> received.</summary>
internal sealed record ReceivedWebhook(IReadOnlyDictionary<string, string> Headers, string Body);
