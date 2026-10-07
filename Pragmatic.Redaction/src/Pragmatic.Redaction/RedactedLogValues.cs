using System.Collections;

namespace Pragmatic.Redaction;

/// <summary>
///     A log entry's structured values with the declared members already masked, and the message
///     rendered from those same masked values.
/// </summary>
/// <remarks>
///     Rendering here rather than reusing the original formatter is the whole point. The formatter
///     closes over the ORIGINAL state, so calling it would put the unmasked value back into the
///     message text while the structured property beside it was masked. Both loggers that redact go
///     through here (<see cref="DeclaredRedactor.RedactState" />): <c>RedactingLogger</c>, and
///     Pragmatic.Logging's own logger, which used to mask <c>LogEntry.Properties</c> after the message
///     was already rendered and so wrote the member in clear.
/// </remarks>
internal sealed class RedactedLogValues : IReadOnlyList<KeyValuePair<string, object?>>
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly KeyValuePair<string, object?>[] _values;
    private string? _rendered;

    public RedactedLogValues(KeyValuePair<string, object?>[] values) => _values = values;

    public int Count => _values.Length;

    public KeyValuePair<string, object?> this[int index] => _values[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        => ((IEnumerable<KeyValuePair<string, object?>>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();

    public override string ToString() => _rendered ??= Render();

    /// <summary>
    ///     Renders the message from the masked values, by position as MEL does (<see cref="LogMessageTemplate" />).
    /// </summary>
    /// <remarks>
    ///     Same output is the point: an entry whose arguments include a declared value must not log its
    ///     other values differently from the same call without one. The tests compare this with MEL's own
    ///     rendering rather than with expected strings.
    /// </remarks>
    private string Render()
    {
        var template = FindTemplate();
        if (template is null)
            return string.Join(" ", _values.Select(v => $"{v.Key}={v.Value}"));

        var arguments = new List<object?>(_values.Length);
        foreach (var value in _values)
            if (!string.Equals(value.Key, OriginalFormatKey, StringComparison.Ordinal))
                arguments.Add(value.Value);

        return LogMessageTemplate.Render(template, arguments);
    }

    private string? FindTemplate()
    {
        foreach (var value in _values)
            if (string.Equals(value.Key, OriginalFormatKey, StringComparison.Ordinal))
                return value.Value as string;

        return null;
    }
}
