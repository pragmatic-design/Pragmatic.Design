namespace Casework.Intake.Entities;

/// <summary>
///     A document attached to a case: what it is called, how long it is, what it hashes to, and where
///     the bytes are.
/// </summary>
/// <remarks>
///     <para>
///         <c>[PartOf&lt;Case&gt;]</c> because it has no life of its own: it is written through its case,
///         in the case's transaction and under the case's permission, and nothing addresses a document
///         without naming the case it belongs to. It sits beside its aggregate, with no folder of its
///         own, for the same reason.
///     </para>
///     <para>
///         ⚠️ The row keeps the storage <b>key</b> as a string and not a path. What
///         <c>IFileStorage.SaveAsync</c> returns is provider-defined — local disk gives a relative URI,
///         Azure Blob an absolute one — so it is persisted as text and read back with
///         <c>new Uri(text, UriKind.RelativeOrAbsolute)</c>. The absolute-only overload throws on the
///         relative shape, on the day the deployment changes store and not before.
///     </para>
///     <para>
///         The hash is not a checksum for its own sake: it is what the download's <c>ETag</c> is, so a
///         client that already has these bytes can be told so, and what makes "the same document came
///         back" a thing a test can assert.
///     </para>
/// </remarks>
[Entity]
[PartOf<Case>]
[Relation.ManyToOne<Case>]
public partial class CaseDocument : IEntity
{
    /// <summary>The name the file had when it arrived — what an operator recognises it by.</summary>
    [Required]
    [MaxLength(255)]
    public string FileName { get; private set; } = "";

    /// <summary>What the client said it is. Recorded, and used when the bytes are served back.</summary>
    [Required]
    [MaxLength(100)]
    public string ContentType { get; private set; } = "";

    /// <summary>How many bytes arrived — counted while reading, not taken from the header.</summary>
    public long Length { get; private set; }

    /// <summary>SHA-256 of the bytes, uppercase hex.</summary>
    [Required]
    [MaxLength(64)]
    public string Sha256 { get; private set; } = "";

    /// <summary>The storage key, as the store returned it. Never composed by hand at a call site.</summary>
    [Required]
    [MaxLength(400)]
    public string StorageKey { get; private set; } = "";

    /// <summary>When it arrived: the application's clock.</summary>
    public DateTimeOffset UploadedOn { get; private set; }

    internal static CaseDocument Stored(
        string fileName, string contentType, long length, string sha256, string storageKey, DateTimeOffset now)
    {
        var document = Create();
        document.SetFileName(fileName);
        document.SetContentType(contentType);
        document.SetLength(length);
        document.SetSha256(sha256);
        document.SetStorageKey(storageKey);
        document.SetUploadedOn(now);

        return document;
    }
}
