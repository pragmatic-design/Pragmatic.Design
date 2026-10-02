using System.Text.Json.Serialization;

namespace Pragmatic.Notifications.Slack;

/// <summary>
///     Source-generated serialization for the Slack payload, so the channel stays AOT- and trim-safe.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SlackPayload))]
internal sealed partial class SlackJsonContext : JsonSerializerContext;
