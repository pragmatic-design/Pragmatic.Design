namespace Pragmatic.SourceGenerator.Features.Logging.Models;

/// <summary>How a property's value is written, decided from its type at compile time.</summary>
internal enum LogValueKind
{
    /// <summary>A <see cref="string" />: transcoded into the message, a JSON string.</summary>
    String,

    /// <summary>A <see cref="bool" />: <c>True</c>/<c>False</c> in the message, a JSON boolean.</summary>
    Boolean,

    /// <summary>A number with a <c>Utf8JsonWriter.WriteNumber</c> overload: a JSON number.</summary>
    Number,

    /// <summary>Any other <c>IUtf8SpanFormattable</c>: formatted, a JSON string.</summary>
    Formattable,

    /// <summary>An enum: by name, without boxing.</summary>
    Enum,

    /// <summary>
    ///     Anything else. The call site cannot write it without serializing it, so the state is not
    ///     self-contained and a Pragmatic provider renders it from the list view.
    /// </summary>
    Object,
}
