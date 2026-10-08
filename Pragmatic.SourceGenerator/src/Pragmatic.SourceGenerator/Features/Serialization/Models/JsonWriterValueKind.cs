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

    /// <summary>
    ///     A <c>DateTime</c>: ISO 8601 with the fraction trimmed. The log profile writes it as text, so it is
    ///     escaped; the response profile through <c>WriteStringValue(DateTime)</c>, as System.Text.Json does.
    /// </summary>
    DateTime,

    /// <summary>A <c>DateTimeOffset</c>: as <see cref="DateTime" />, the <c>+</c> of the offset included.</summary>
    DateTimeOffset,

    /// <summary>A <c>TimeSpan</c>: a string in the constant format, as System.Text.Json writes it.</summary>
    TimeSpan,

    /// <summary>A <c>DateOnly</c>: <c>yyyy-MM-dd</c>, as System.Text.Json writes it.</summary>
    DateOnly,

    /// <summary>A <c>TimeOnly</c>: as System.Text.Json writes it.</summary>
    TimeOnly,

    /// <summary>A <c>Uri</c>: its original string, as System.Text.Json writes it.</summary>
    Uri,

    /// <summary>A <c>byte[]</c>: a base64 string, as System.Text.Json writes it.</summary>
    Base64,

    /// <summary>An enum: its underlying number, which is what the declared redactor's options write.</summary>
    Enum,

    /// <summary>
    ///     An enum by name, as <c>JsonStringEnumConverter</c> writes it: a declared member as its name in
    ///     <see cref="JsonWriterValueModel.EnumNames" />, anything else as its number.
    /// </summary>
    EnumName,

    /// <summary>An object with a writer method of its own: <see cref="JsonWriterValueModel.Method" />.</summary>
    Object,

    /// <summary>A sequence: a JSON array of <see cref="JsonWriterValueModel.Element" />.</summary>
    Collection,

    /// <summary>A dictionary keyed by string: a JSON object whose values are <see cref="JsonWriterValueModel.Element" />.</summary>
    Dictionary,

    /// <summary>The redaction mask, in place of a value the type declared must not be logged.</summary>
    Mask,
}
