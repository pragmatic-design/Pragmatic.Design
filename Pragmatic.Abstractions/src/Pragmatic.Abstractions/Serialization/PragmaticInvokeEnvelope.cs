using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pragmatic.Serialization;

/// <summary>
///     The request body of <c>POST /_pragmatic/invoke</c>: which action, and its payload.
/// </summary>
/// <remarks>
///     <para>
///         A named record rather than an anonymous object. An anonymous type cannot appear in a
///         source-generated <c>JsonSerializerContext</c> — there is no name to write in a
///         <c>[JsonSerializable]</c> — so every remote call would go through reflection, which a
///         Native AOT publish does not have.
///     </para>
///     <para>
///         It lives here, and not beside the endpoint that receives it, because the sending side is
///         deliberately self-contained: a module that calls a remote boundary references
///         <c>HttpClient</c> and this assembly, not the host.
///     </para>
/// </remarks>
/// <param name="ActionType">The action's fully-qualified type name, as the host registered it.</param>
/// <param name="Payload">The serialized action.</param>
public sealed record PragmaticInvokeEnvelope(
    [property: JsonPropertyName("actionType")] string ActionType,
    [property: JsonPropertyName("payload")] JsonElement Payload);
