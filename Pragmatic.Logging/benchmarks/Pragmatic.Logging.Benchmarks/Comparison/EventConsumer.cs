using System.Globalization;
using System.Text;

namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>
///     The work every sink in the comparison does with an event: the rendered message into a reused
///     buffer, then every structured property, key and value, formatted into the same buffer.
/// </summary>
/// <remarks>
///     <para>
///         One implementation for all four libraries, so the sinks differ only in how they reach the
///         event's message and properties — which is the library's own cost, and what the comparison is
///         for. A sink that discards the event measures how cheaply a library can skip the work, and the
///         numbers this replaced compared exactly that against libraries doing it.
///     </para>
///     <para>
///         Values are formatted with <see cref="ISpanFormattable" /> into a stack buffer where they can be,
///         so the consumer itself allocates nothing for numbers and dates; the allocations a row reports
///         are the library's.
///     </para>
///     <para>
///         <see cref="Capture" /> is switched on only by the equivalence check in <c>GlobalSetup</c>: it
///         records what one call produced so the four sinks can be compared before anything is timed.
///     </para>
/// </remarks>
internal sealed class EventConsumer
{
    private readonly List<string> _properties = [];
    private string? _exception;

    /// <summary>The reused buffer the message and the properties are written into.</summary>
    public StringBuilder Buffer { get; } = new(512);

    /// <summary>When set, <see cref="Complete" /> records the event for <see cref="TakeLast" />.</summary>
    public bool Capture { get; set; }

    /// <summary>Sum of everything written, read once at the end so the work cannot be optimized away.</summary>
    public long Checksum { get; private set; }

    private ConsumedEvent? _last;
    private int _messageLength;

    /// <summary>Starts an event: clears the buffer the previous one used.</summary>
    public void Begin()
    {
        Buffer.Clear();
        _properties.Clear();
        _exception = null;
    }

    /// <summary>Marks where the message ends, for a sink that rendered it into <see cref="Buffer" /> itself.</summary>
    public void EndMessage() => _messageLength = Buffer.Length;

    /// <summary>Writes an already rendered message.</summary>
    public void Message(string message)
    {
        Buffer.Append(message);
        EndMessage();
    }

    /// <summary>Writes a message a library rendered as UTF-8, decoding it into the buffer.</summary>
    public void Utf8Message(ReadOnlySpan<byte> utf8)
    {
        Span<char> chars = utf8.Length <= 256 ? stackalloc char[256] : new char[utf8.Length];
        var count = Encoding.UTF8.GetChars(utf8, chars);
        Buffer.Append(chars[..count]);
        EndMessage();
    }

    /// <summary>
    ///     Whether <paramref name="key" /> is something a library adds on its own rather than a property of
    ///     the call — the category, the event id, MEL's template. Not read by any sink, so the property set
    ///     each one compares is the call's, and the work each one does is the same.
    /// </summary>
    public static bool IsLibraryMetadata(string key)
        => key is "{OriginalFormat}" or "SourceContext" or "EventId" or "EventId_Id" or "EventId_Name" or "EventName";

    /// <summary>Writes one structured property, key and value.</summary>
    public void Property(string key, object? value)
    {
        var start = Buffer.Length;
        Buffer.Append(' ').Append(key).Append('=');
        AppendValue(value);

        if (Capture)
            _properties.Add(Buffer.ToString(start + 1, Buffer.Length - start - 1));
    }

    /// <summary>Writes the exception the event carries.</summary>
    public void Exception(Exception? exception)
    {
        if (exception is null)
            return;

        Buffer.Append(' ').Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        if (Capture)
            _exception = $"{exception.GetType().Name}: {exception.Message}";
    }

    /// <summary>Ends an event.</summary>
    public void Complete()
    {
        Checksum += Buffer.Length;
        if (Capture)
            _last = new ConsumedEvent(Buffer.ToString(0, _messageLength), [.. _properties.Order(StringComparer.Ordinal)], _exception);
    }

    /// <summary>The event recorded by the last <see cref="Complete" /> while <see cref="Capture" /> was on.</summary>
    public ConsumedEvent? TakeLast()
    {
        var last = _last;
        _last = null;
        return last;
    }

    private void AppendValue(object? value)
    {
        switch (value)
        {
            case null:
                Buffer.Append("null");
                return;
            case string text:
                Buffer.Append(text);
                return;
            case ISpanFormattable formattable:
                Span<char> scratch = stackalloc char[64];
                if (formattable.TryFormat(scratch, out var written, default, CultureInfo.InvariantCulture))
                {
                    Buffer.Append(scratch[..written]);
                    return;
                }

                Buffer.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            default:
                Buffer.Append(value.ToString());
                return;
        }
    }
}
