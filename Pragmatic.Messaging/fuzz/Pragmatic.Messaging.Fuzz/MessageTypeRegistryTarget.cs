using System.Text;
using System.Text.Json;
using Pragmatic.Messaging.Generated;

namespace Pragmatic.Messaging.Fuzz;

/// <summary>
///     The generated registry on a type name and a payload another service wrote (#312). The input is the type name,
///     a newline, and the payload; without a newline it is all payload, for the one type this registry knows.
/// </summary>
/// <remarks>
///     The declared outcomes are the message, null for a type the registry does not know, and a
///     <see cref="JsonException" />, which every caller turns into a retry or a dead letter. Those return; anything
///     else escapes and is a crash. The registry is in this assembly, which is not instrumented: the coverage that
///     steers libFuzzer comes from <c>Pragmatic.Messaging.Core.dll</c>.
/// </remarks>
public static class MessageTypeRegistryTarget
{
    private static readonly PragmaticMessageTypeRegistry Registry = new();

    public static void Run(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        var (typeName, payload) = newline < 0
            ? (typeof(FuzzOrderPlaced).FullName!, text)
            : (text[..newline], text[(newline + 1)..]);

        try
        {
            if (Registry.Deserialize(typeName, payload) is { } message && message.GetType().FullName != typeName)
                throw new InvalidOperationException($"'{typeName}' was read as a {message.GetType()}.");
        }
        catch (JsonException)
        {
        }
    }
}
