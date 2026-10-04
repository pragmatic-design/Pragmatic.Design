namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>What one sink made of one event: the rendered message, the properties it read, the exception.</summary>
/// <param name="Message">The message as the library rendered it.</param>
/// <param name="Properties">Every property as <c>key=value</c>, sorted, values formatted invariantly.</param>
/// <param name="Exception">The exception's type and message, when the event carried one.</param>
internal sealed record ConsumedEvent(string Message, string[] Properties, string? Exception)
{
    /// <inheritdoc />
    public override string ToString()
        => $"\"{Message}\" [{string.Join(", ", Properties)}]{(Exception is null ? "" : $" {Exception}")}";

    /// <summary>Whether two sinks produced the same message, the same property set and the same exception.</summary>
    public bool SameAs(ConsumedEvent other)
        => Message == other.Message && Properties.SequenceEqual(other.Properties) && Exception == other.Exception;
}
