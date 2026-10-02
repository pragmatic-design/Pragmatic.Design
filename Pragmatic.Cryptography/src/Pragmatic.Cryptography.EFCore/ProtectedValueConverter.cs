using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pragmatic.Cryptography.EFCore;

/// <summary>
///     Maps <see cref="ProtectedValue" /> to the <c>byte[]</c> column that stores it.
/// </summary>
/// <remarks>
///     <para>
///         <b>This converter performs no cryptography, and that is deliberate.</b> A value converter is
///         a compiled expression invoked synchronously inside EF's pipeline, while resolving a subject's
///         key is a database read — and, more importantly, reading protected data has three outcomes,
///         one of which is "this subject was erased". A converter can return a value or throw; it cannot
///         say "erased", so making it decrypt would collapse an expected outcome into what looks like an
///         attack.
///     </para>
///     <para>
///         Encryption and decryption stay explicit, on <see cref="ISubjectDataProtector" />, where the
///         outcome is visible and the caller has to handle it. This converter only moves bytes.
///     </para>
/// </remarks>
public sealed class ProtectedValueConverter() : ValueConverter<ProtectedValue, byte[]>(
    v => v.Packed ?? new byte[0],
    v => new ProtectedValue(v));
