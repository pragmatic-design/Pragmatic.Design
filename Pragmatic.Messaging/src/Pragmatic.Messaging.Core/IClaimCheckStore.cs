namespace Pragmatic.Messaging;

/// <summary>
///     Claim check pattern: payloads above the configured threshold are stored OUT of band
///     (blob/file storage) and the message carries only a reference (<c>x-claim-check</c>
///     header) — brokers stay fast and size limits stop mattering. The consume side retrieves
///     transparently before deserialization.
/// </summary>
public interface IClaimCheckStore
{
    /// <summary>Header carrying the claim-check reference on checked messages.</summary>
    const string HeaderName = "x-claim-check";

    /// <summary>
    ///     Stores a payload stream and returns its reference. The store reads the stream to completion;
    ///     the caller owns and disposes it. Streaming avoids buffering the (by-definition large) payload.
    /// </summary>
    Task<string> StoreAsync(Stream payload, CancellationToken ct = default);

    /// <summary>
    ///     Retrieves a payload as a stream by reference. The CALLER owns and disposes the returned stream.
    ///     Streaming lets the consumer deserialize without materialising the whole payload in memory.
    /// </summary>
    Task<Stream> RetrieveAsync(string reference, CancellationToken ct = default);

    /// <summary>Deletes a stored payload (post-consume cleanup). Idempotent.</summary>
    Task DeleteAsync(string reference, CancellationToken ct = default);
}
