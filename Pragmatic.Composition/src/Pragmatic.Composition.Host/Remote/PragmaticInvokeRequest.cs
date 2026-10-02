using System.Text.Json;

namespace Pragmatic.Composition.Remote;

/// <summary>
///     Wire format for the <c>/_pragmatic/invoke</c> endpoint.
///     Carries the action type discriminator and the serialized action payload.
/// </summary>
/// <param name="ActionType">
///     The fully-qualified action type name used as the dispatch discriminator on the receiving host.
///     Must match a type the remote boundary knows how to invoke.
/// </param>
/// <param name="Payload">The action instance serialized as JSON, deserialized server-side into the action type.</param>
public sealed record PragmaticInvokeRequest(string ActionType, JsonElement Payload);
