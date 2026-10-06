using System.Text.Json;

namespace Pragmatic.Logging.CallSites;

/// <summary>
///     Writes a call site's state of type <typeparamref name="TState" />, taken by reference: how a provider
///     reaches a generated state without boxing it.
/// </summary>
/// <remarks>
///     <para>
///         A provider receives the state as a type parameter of <c>ILogger.Log&lt;TState&gt;</c>. Testing it
///         for <see cref="IUtf8LogState" /> costs nothing, but casting it to the interface to call a member
///         boxes it: measured at 80 bytes a call through the JSON provider, with every method compiled
///         optimized, because the boxed reference is handed on and the JIT keeps it. So the generated state
///         registers a writer for its own type once, in its type initializer, and the provider looks the
///         writer up by type parameter (<see cref="Utf8LogStateWriters{TState}" />) and hands it the state
///         as an <c>in</c> argument. No cast, no box, no reflection.
///     </para>
///     <para>
///         The members are the call site's constants and <see cref="IUtf8LogState" />'s, read through the
///         writer.
///     </para>
/// </remarks>
public interface IUtf8LogStateWriter<TState>
{
    /// <inheritdoc cref="IUtf8LogState.Template" />
    string Template { get; }

    /// <inheritdoc cref="IUtf8LogState.IsSelfContained" />
    bool IsSelfContained { get; }

    /// <inheritdoc cref="IUtf8LogState.PropertyCount" />
    int PropertyCount { get; }

    /// <inheritdoc cref="IUtf8LogState.TryFormatMessage" />
    bool TryFormatMessage(in TState state, Span<byte> destination, out int bytesWritten);

    /// <inheritdoc cref="IUtf8LogState.WriteProperties" />
    void WriteProperties(in TState state, Utf8JsonWriter writer);
}
