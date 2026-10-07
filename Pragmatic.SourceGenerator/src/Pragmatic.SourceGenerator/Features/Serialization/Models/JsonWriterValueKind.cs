namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>How a generated UTF-8 JSON writer writes one value, decided from its type at compile time.</summary>
internal enum JsonWriterValueKind
{
    /// <summary>A <see cref="string" />: a JSON string, or null.</summary>
    String,

    /// <summary>A <see cref="bool" />: a JSON boolean.</summary>
    Boolean,

    /// <summary>A number with a <c>WriteNumberValue</c> overload, widened to <see cref="JsonWriterValueModel.Cast" />.</summary>
    Number,

    /// <summary>A <see cref="char" />: a one-character string, as System.Text.Json writes it.</summary>
    Char,

    /// <summary>A <c>Guid</c>: <c>WriteStringValue</c> has an overload, and none of its characters is escaped.</summary>
    StringValue,

    /// <summary>A <c>DateTime</c>: ISO 8601 with the fraction trimmed, written as text so it is escaped.</summary>
    DateTime,

    /// <summary>A <c>DateTimeOffset</c>: as <see cref="DateTime" />; the <c>+</c> of the offset is escaped.</summary>
    DateTimeOffset,

    /// <summary>A <c>TimeSpan</c>: a string in the constant format, as System.Text.Json writes it.</summary>
    TimeSpan,

    /// <summary>An enum: its underlying number, which is what the declared redactor's options write.</summary>
    Enum,

    /// <summary>An object with a writer method of its own: <see cref="JsonWriterValueModel.Method" />.</summary>
    Object,

    /// <summary>An array or a <c>List&lt;T&gt;</c>: a JSON array of <see cref="JsonWriterValueModel.Element" />.</summary>
    Collection,

    /// <summary>The redaction mask, in place of a value the type declared must not be logged.</summary>
    Mask,
}
