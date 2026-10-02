using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Audit;

/// <summary>
///     Hashes a value the trail records a change to, without the trail retaining it.
/// </summary>
/// <remarks>
///     <para>
///         What <see cref="AuditEntry.ValueHash" /> is for. Anyone who can produce a candidate can be
///         told whether it is what was there; nobody can read what was there out of the trail. That is
///         the trade an append-only, long-retained record has to make with values it did not choose and
///         cannot classify.
///     </para>
///     <para>
///         <b>Unsalted, deliberately, and that has a consequence worth stating.</b> A hash the writer
///         salts per entry answers nothing later, because verification happens elsewhere and much
///         later, without the salt. The cost is that a value from a small or guessable set — a boolean
///         setting, a short enum, a port number — can be recovered by trying candidates. For
///         configuration that is acceptable: the point is proving a change, not concealing that a
///         timeout was 30 seconds. It is <em>not</em> acceptable for anything secret, which is why
///         callers pass a placeholder for those instead of the value.
///     </para>
/// </remarks>
public static class AuditValueHash
{
    /// <summary>SHA-256 of the value, or <see langword="null" /> when there was no value.</summary>
    public static byte[]? Of(string? value)
        => value is null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
