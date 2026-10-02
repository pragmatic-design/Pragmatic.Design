namespace Pragmatic.Email;

/// <summary>
///     Typed email address with optional display name.
/// </summary>
/// <remarks>
///     <para>
///         <b>Only <see cref="Validated"/> validates.</b> The implicit conversion from
///         <see cref="string"/> and the primary constructor accept anything, so an address obtained
///         that way — or an <see cref="EmailMessage"/> built through its initializer rather than the
///         builder — is never checked. Prefer
///         <see cref="Pragmatic.Email.Builder.EmailMessageBuilder"/>, which calls
///         <see cref="Validated"/> on every address as you add it, so a malformed one fails at the
///         line that introduced it.
///     </para>
///     <para>
///         Skipping validation is not a security hole: the MIME writer and the SMTP transport strip
///         control characters from addresses before they reach the wire, so a CRLF cannot inject a
///         header or an SMTP command either way. What is lost is the early, precise error — an
///         unvalidated address simply gets rejected later by the receiving server.
///     </para>
/// </remarks>
public readonly record struct EmailAddress(string Address, string? DisplayName = null)
{
    /// <summary>
    ///     Validates that <paramref name="address"/> is minimally well-formed:
    ///     non-empty, free of control characters, and contains '@' with a non-empty
    ///     local-part and domain. Throws <see cref="ArgumentException"/> for malformed addresses.
    /// </summary>
    /// <remarks>
    ///     Rejecting control characters (notably CR/LF) closes an SMTP/MIME header-injection
    ///     vector: an address such as <c>"a@b.com\r\nBcc: victim@x"</c> would otherwise inject
    ///     extra headers or SMTP commands at the transport sinks.
    /// </remarks>
    public static EmailAddress Validated(string address, string? displayName = null)
    {
        Pragmatic.Ensure.Ensure.ThrowIfNullOrWhiteSpace(address, nameof(address));

        foreach (var c in address)
            if (c < ' ')
                throw new ArgumentException(
                    $"'{address}' is not a valid email address: must not contain control characters (e.g. CR/LF).",
                    nameof(address));

        var atIndex = address.IndexOf('@');
        if (atIndex <= 0 || atIndex == address.Length - 1)
            throw new ArgumentException(
                $"'{address}' is not a valid email address: must contain '@' with a non-empty local-part and domain.",
                nameof(address));

        return new EmailAddress(address, displayName);
    }

    public override string ToString() => DisplayName is not null
        ? $"{DisplayName} <{Address}>"
        : Address;

    public static implicit operator EmailAddress(string address) => new(address);
}
