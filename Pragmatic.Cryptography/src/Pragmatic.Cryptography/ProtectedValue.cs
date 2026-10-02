namespace Pragmatic.Cryptography;

/// <summary>
///     A packed ciphertext produced by <see cref="ISubjectDataProtector" />, as it sits in storage.
/// </summary>
/// <remarks>
///     <para>
///         This type carries no key and performs no cryptography. It exists so a protected column reads
///         as protected — <c>ProtectedValue Email</c> rather than an anonymous <c>byte[]</c> that any
///         caller might be tempted to treat as text.
///     </para>
///     <para>
///         <b>There is deliberately no implicit conversion to a plaintext string.</b> Reading protected
///         data is asynchronous (the key must be resolved) and has three possible outcomes, one of which
///         is "this subject was erased". A property getter can express neither. Read it through
///         <see cref="ISubjectDataProtector.TryReadAsync" /> and handle the outcome.
///     </para>
/// </remarks>
public readonly record struct ProtectedValue(byte[] Packed)
{
    /// <summary>An absent value — no ciphertext at all, as distinct from one that cannot be read.</summary>
    public static ProtectedValue None { get; } = new([]);

    /// <summary>True when there is no ciphertext stored.</summary>
    public bool IsEmpty => Packed is null || Packed.Length == 0;

    /// <summary>
    ///     The id of the key that produced this value, or <see langword="null" /> when the value carries
    ///     no header. Readable without any key — it is what a reader uses to find out whether the
    ///     subject still exists.
    /// </summary>
    public string? KeyId => !IsEmpty && CiphertextHeader.TryReadKeyId(Packed, out var id) ? id : null;

    /// <summary>The stored bytes, for persistence.</summary>
    public static implicit operator byte[](ProtectedValue value) => value.Packed ?? [];

    /// <summary>Wraps stored bytes.</summary>
    public static explicit operator ProtectedValue(byte[] packed) => new(packed);
}
