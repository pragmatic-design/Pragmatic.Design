// Pragmatic.Design - Metadata JSON Builder
// Shared helper to build JSON for [PragmaticMetadata] attributes

using System.Text;

namespace Pragmatic.SourceGen;

/// <summary>
///     Builds JSON payloads for [PragmaticMetadata] attributes.
///     Handles formatting (indented in Debug, minified in Release) and escaping.
/// </summary>
internal sealed class MetadataJsonBuilder
{
    private readonly bool _indent;
    private readonly StringBuilder _sb = new();
    private int _depth;
    private bool _needsComma;

    /// <summary>
    ///     Creates a new JSON builder.
    /// </summary>
    /// <param name="indent">Whether to format with indentation (Debug mode).</param>
    public MetadataJsonBuilder(bool indent = false)
    {
        _indent = indent;
    }

    /// <summary>
    ///     Starts a JSON object.
    /// </summary>
    public MetadataJsonBuilder StartObject()
    {
        AppendCommaIfNeeded();
        _sb.Append('{');
        _depth++;
        _needsComma = false;
        return this;
    }

    /// <summary>
    ///     Ends a JSON object.
    /// </summary>
    public MetadataJsonBuilder EndObject()
    {
        _depth--;
        AppendNewLineIfIndented();
        AppendIndent();
        _sb.Append('}');
        _needsComma = true;
        return this;
    }

    /// <summary>
    ///     Starts a JSON array.
    /// </summary>
    public MetadataJsonBuilder StartArray()
    {
        AppendCommaIfNeeded();
        _sb.Append('[');
        _depth++;
        _needsComma = false;
        return this;
    }

    /// <summary>
    ///     Ends a JSON array.
    /// </summary>
    public MetadataJsonBuilder EndArray()
    {
        _depth--;
        AppendNewLineIfIndented();
        AppendIndent();
        _sb.Append(']');
        _needsComma = true;
        return this;
    }

    /// <summary>
    ///     Writes a property name.
    /// </summary>
    public MetadataJsonBuilder Property(string name)
    {
        AppendCommaIfNeeded();
        AppendNewLineIfIndented();
        AppendIndent();
        _sb.Append('"');
        _sb.Append(EscapeString(name));
        _sb.Append("\":");
        if (_indent)
            _sb.Append(' ');
        _needsComma = false;
        return this;
    }

    /// <summary>
    ///     Writes a string value.
    /// </summary>
    public MetadataJsonBuilder Value(string? value)
    {
        AppendCommaIfNeeded();
        if (value is null)
        {
            _sb.Append("null");
        }
        else
        {
            _sb.Append('"');
            _sb.Append(EscapeString(value));
            _sb.Append('"');
        }

        _needsComma = true;
        return this;
    }

    /// <summary>
    ///     Writes an integer value.
    /// </summary>
    public MetadataJsonBuilder Value(int value)
    {
        AppendCommaIfNeeded();
        _sb.Append(value);
        _needsComma = true;
        return this;
    }

    /// <summary>
    ///     Writes a long integer value.
    /// </summary>
    public MetadataJsonBuilder Value(long value)
    {
        AppendCommaIfNeeded();
        _sb.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _needsComma = true;
        return this;
    }

    /// <summary>
    ///     Writes a floating-point value as a JSON number. A whole number renders without a fraction,
    ///     so a bound declared as an <c>int</c> reads back as one.
    /// </summary>
    public MetadataJsonBuilder Value(double value)
    {
        AppendCommaIfNeeded();
        _sb.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        _needsComma = true;
        return this;
    }

    /// <summary>
    ///     Writes a boolean value.
    /// </summary>
    public MetadataJsonBuilder Value(bool value)
    {
        AppendCommaIfNeeded();
        _sb.Append(value ? "true" : "false");
        _needsComma = true;
        return this;
    }

    /// <summary>
    ///     Writes a raw JSON fragment inline (not escaped as a string).
    ///     Use when the value is already valid JSON and must be embedded as an object/array.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     <paramref name="rawJson" /> is not a structurally valid JSON value. Emitting it verbatim would
    ///     produce a corrupt document, and the corruption only surfaces far downstream (a consumer parsing
    ///     the manifest or the OpenAPI document at runtime), so the fragment is validated here instead of
    ///     being trusted. Generator outputs run under <see cref="SafeSourceOutput" />, which turns the
    ///     throw into a PRAG9000 diagnostic on the offending output rather than a build-wide crash.
    /// </exception>
    public MetadataJsonBuilder RawValue(string rawJson)
    {
        if (!JsonValidator.IsValid(rawJson))
            throw new ArgumentException(
                $"RawValue requires a structurally valid JSON fragment, got: {Preview(rawJson)}", nameof(rawJson));

        AppendCommaIfNeeded();
        _sb.Append(rawJson);
        _needsComma = true;
        return this;
    }

    private static string Preview(string? value)
    {
        if (value is null)
            return "<null>";
        return value.Length <= 120 ? value : value.Substring(0, 120) + "…";
    }

    /// <summary>
    ///     Writes a null value.
    /// </summary>
    public MetadataJsonBuilder Null()
    {
        AppendCommaIfNeeded();
        _sb.Append("null");
        _needsComma = true;
        return this;
    }

    /// <summary>
    ///     Writes a property with string value.
    /// </summary>
    public MetadataJsonBuilder Property(string name, string? value)
    {
        Property(name);
        Value(value);
        return this;
    }

    /// <summary>
    ///     Writes a property with integer value.
    /// </summary>
    public MetadataJsonBuilder Property(string name, int value)
    {
        Property(name);
        Value(value);
        return this;
    }

    /// <summary>
    ///     Writes a property with boolean value.
    /// </summary>
    public MetadataJsonBuilder Property(string name, bool value)
    {
        Property(name);
        Value(value);
        return this;
    }

    /// <summary>
    ///     Writes a property with null value.
    /// </summary>
    public MetadataJsonBuilder PropertyNull(string name)
    {
        Property(name);
        Null();
        return this;
    }

    /// <summary>
    ///     Writes a string array property.
    /// </summary>
    public MetadataJsonBuilder PropertyArray(string name, IEnumerable<string> values)
    {
        Property(name);
        StartArray();
        foreach (var value in values)
        {
            AppendNewLineIfIndented();
            AppendIndent();
            Value(value);
        }

        EndArray();
        return this;
    }

    /// <summary>
    ///     Returns the built JSON string.
    /// </summary>
    public override string ToString()
    {
        return _sb.ToString();
    }

    /// <summary>
    ///     Returns the JSON string escaped for use in a C# raw string literal.
    /// </summary>
    public string ToEscapedString()
    {
        // For raw string literals, we just need to handle quotes at start/end
        var json = ToString();
        // No escaping needed for raw strings, but ensure no """ appears
        return json.Replace("\"\"\"", "\\\"\\\"\\\"");
    }

    private void AppendCommaIfNeeded()
    {
        if (_needsComma)
            _sb.Append(',');
    }

    private void AppendNewLineIfIndented()
    {
        if (_indent)
            _sb.AppendLine();
    }

    private void AppendIndent()
    {
        if (_indent)
            _sb.Append(new string(' ', _depth * 2));
    }

    private static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }

        return sb.ToString();
    }
}

/// <summary>
///     Schema versions for metadata categories.
/// </summary>
internal static class MetadataSchemaVersions
{
    public const string Di = "1.0.0";
    public const string Mapping = "1.0.0";
    public const string Actions = "1.0.0";
    public const string Startup = "1.0.0";
    public const string Validation = "1.0.0";
    public const string Endpoints = "1.0.0";
    public const string HealthChecks = "1.0.0";
    public const string Identifiers = "1.0.0";
    public const string Module = "1.0.0";
    public const string EventHandlers = "1.0.0";
    public const string Caching = "1.0.0";
    public const string Persistence = "1.2.0";
    public const string Configuration = "1.0.0";
    public const string HostTopology = "1.0.0";
    public const string Authorization = "1.0.0";
    public const string PersonalData = "1.0";
}