namespace Pragmatic.Messaging.Configuration;

/// <summary>
///     Configuration for the claim check pattern (see <see cref="IClaimCheckStore"/>).
/// </summary>
/// <remarks>
///     <b>Data-at-rest posture.</b> A checked blob is the <em>complete serialized message</em>. The
///     <c>[NotLogged]</c> redaction applies to audit/log serialization only — it does NOT strip fields
///     from the claim-checked payload, so any secret or PII the message carries is written to the
///     backing store verbatim. Encrypting that store is the storage provider's job (Azure Blob /
///     S3 server-side encryption, or an encrypted volume for local disk) — the framework deliberately
///     does not embed key management. Bound the exposure window with <see cref="Retention"/>.
/// </remarks>
public sealed class ClaimCheckOptions
{
    /// <summary>Payloads strictly larger than this are checked into the store. Default: 256 KiB.</summary>
    public int Threshold { get; set; } = 256 * 1024;

    /// <summary>
    ///     Deletes the stored payload after a SUCCESSFUL consume. On failure the blob stays so the
    ///     transport redelivery can re-read it. Default: <c>false</c> — safe for fan-out (topic → many
    ///     subscribers), where deleting after the first consume would starve the others. Set to
    ///     <c>true</c> only for competing-consumer topologies (a single logical consumer) to reclaim
    ///     storage eagerly; pair it with idempotency so a redelivered duplicate whose blob is gone is
    ///     dropped rather than poisoned.
    /// </summary>
    public bool DeleteAfterConsume { get; set; }

    /// <summary>
    ///     How long a checked payload should remain readable before it is expired. Default: 7 days.
    /// </summary>
    /// <remarks>
    ///     With <see cref="DeleteAfterConsume"/> off (the fan-out-safe default) nothing deletes blobs
    ///     on the happy path, so without expiry the store grows without bound and keeps full message
    ///     payloads — see the data-at-rest note on <see cref="ClaimCheckOptions"/>.
    ///     <para>
    ///     <b>This value is declarative.</b> <c>IFileStorage</c> exposes no enumeration, so the
    ///     framework cannot sweep expired blobs itself; configure a matching lifecycle rule on the
    ///     backing store (Azure Blob lifecycle management, S3 lifecycle expiration, or a cron/tmpfiles
    ///     rule for local disk). Keep it comfortably longer than the transport's maximum redelivery
    ///     window, or a legitimately retried message will find its payload already expired.
    ///     </para>
    /// </remarks>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}
