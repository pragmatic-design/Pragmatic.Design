using System.Text.Json;

namespace Pragmatic.Serialization;

/// <summary>
///     The serializer options for the action payload of a remote boundary call.
/// </summary>
/// <remarks>
///     <para>
///         The payload travels inside the invoke envelope as a <see cref="JsonElement" />. Serializing it
///         with the no-options overload on each end would make the property names PascalCase by accident
///         rather than by decision, and the contract invisible: giving those calls typed metadata would
///         silently switch them to camelCase, and the receiving action would come back with an empty
///         property, not an error.
///     </para>
///     <para>
///         So the contract is stated here, once, and both ends use it. The resolver comes from the
///         shared seam, which is what makes the same call work under Native AOT.
///     </para>
/// </remarks>
public static class PragmaticRemotePayload
{
    /// <summary>Options matching the wire format of the action payload.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        // Deliberately not camelCase: the receiving side reads the payload with the default naming
        // policy, and changing it here would change the wire format of every remote call.
        PropertyNamingPolicy = null,
        TypeInfoResolver = PragmaticJsonOptions.Default.Build().TypeInfoResolver,
    };
}
