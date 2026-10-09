using System.Text.Json;

namespace Pragmatic.Logging.CallSites;

/// <summary>
///     A log call's state that writes itself as UTF-8: the message rendered into a byte buffer and the
///     structured properties into a <see cref="Utf8JsonWriter" />, without boxing a value.
/// </summary>
/// <remarks>
///     <para>
///         Implemented by the <c>readonly struct</c> the generator emits for each <c>[LoggerMessage]</c>
///         call site. The state still reaches the provider through <c>ILogger.Log&lt;TState&gt;</c>, and
///         still implements <c>IReadOnlyList&lt;KeyValuePair&lt;string, object?&gt;&gt;</c> for any
///         provider that reads it the usual way. Generated code never checks: deciding is the provider's.
///     </para>
///     <para>
///         ⚠️ A provider on a hot path reads the state through <see cref="IUtf8LogStateWriter{TState}" />,
///         not through this interface: casting a struct <c>TState</c> to it boxes the state on every call.
///         This one is for code that already holds the state as an object.
///     </para>
///     <para>
///         <b>Redaction is already applied.</b> An argument whose parameter is marked
///         <c>[NotLogged]</c> or <c>[PersonalData]</c> is written as
///         <see cref="Pragmatic.Serialization.RedactionMask" /> by every member here and by the list view,
///         so a provider has nothing to decide for it.
///     </para>
/// </remarks>
public interface IUtf8LogState
{
    /// <summary>The message template as the call site declared it.</summary>
    string Template { get; }

    /// <summary>
    ///     Whether every argument is written by the state itself. False when one is a value of a type the
    ///     call site cannot write without serializing it; a provider then reads the list view, where
    ///     the members its type declared are redacted at runtime.
    /// </summary>
    /// <remarks>A constant of the call site: the generator decides it from the parameter types.</remarks>
    bool IsSelfContained { get; }

    /// <summary>How many structured properties <see cref="WriteProperties" /> writes.</summary>
    int PropertyCount { get; }

    /// <summary>Renders the message as UTF-8 into <paramref name="destination" />.</summary>
    /// <returns>False, with nothing to rely on in <paramref name="destination" />, when it is too small.</returns>
    bool TryFormatMessage(Span<byte> destination, out int bytesWritten);

    /// <summary>Writes each structured property as a JSON property of the object the writer is in.</summary>
    void WriteProperties(Utf8JsonWriter writer);

    /// <summary>
    ///     Whether <see cref="TryFormatMessageAndJson" /> can write this call site's properties as JSON bytes: every
    ///     argument a number, a string, a boolean or masked, every name one no encoder escapes.
    /// </summary>
    /// <remarks>A constant of the call site: the generator decides it from the parameter types and names.</remarks>
    bool WritesJsonProperties { get; }

    /// <summary>
    ///     Renders the message as <see cref="TryFormatMessage" /> does, and the structured properties as the members
    ///     of a JSON object without its braces — <c>"Name":value,…</c> — each value formatted once for both: the bytes
    ///     a <see cref="Utf8JsonWriter" /> with <see cref="System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping" />
    ///     writes for them.
    /// </summary>
    Utf8LogJsonStatus TryFormatMessageAndJson(Span<byte> message, Span<byte> json, out int messageWritten, out int jsonWritten);
}
