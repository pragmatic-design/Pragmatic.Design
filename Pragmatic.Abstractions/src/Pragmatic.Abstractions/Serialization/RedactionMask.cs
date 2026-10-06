namespace Pragmatic.Serialization;

/// <summary>What a value that must not be written out is written as instead.</summary>
/// <remarks>
///     <para>
///         One value, here, because two mechanisms write it: the declared redactor masks a member of a
///         serialized value at runtime, and a generated log call site masks an argument it knows at
///         compile time is <c>[NotLogged]</c> or <c>[PersonalData]</c>. Two copies of the literal is how
///         a search through the logs for one of them stops finding the other.
///     </para>
///     <para>
///         It lives in the abstractions because the generated call site does: a module that logs
///         references this assembly, not the redaction package.
///     </para>
/// </remarks>
public static class RedactionMask
{
    /// <summary>The mask.</summary>
    public const string Value = "[redacted]";

    /// <summary>The mask as UTF-8, for a writer that never holds a <see cref="string" />.</summary>
    public static ReadOnlySpan<byte> Utf8 => "[redacted]"u8;
}
