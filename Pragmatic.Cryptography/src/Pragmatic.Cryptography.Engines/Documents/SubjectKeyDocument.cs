using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Cryptography.Engines.Documents;

/// <summary>
///     A subject's key on the documents engine (collection <c>__SubjectKeys</c>): <see cref="Sql.SubjectKeyRow" />'s fields, the subject as the
///     document's id (one key per subject), the wrapped key as Base64 (the engine stores no byte array). The key id is unique (the collection's
///     index): reads resolve by it.
/// </summary>
[Document("__SubjectKeys")]
public sealed partial class SubjectKeyDocument
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>.</summary>
    public static readonly DocCollectionSchema Schema = new("__SubjectKeys", "/id", [new DocIndex("IX___SubjectKeys_KeyId", ["KeyId"], Unique: true)], []);

    /// <summary>Opaque reference to the subject: a pseudonym, never an identity.</summary>
    [Key]
    public string SubjectRef { get; set; } = "";

    /// <summary>The fingerprint of the key material, which a ciphertext header carries.</summary>
    public string KeyId { get; set; } = "";

    /// <summary>The wrapped key in Base64; null once destroyed.</summary>
    public string? WrappedKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the key was destroyed; null while it is live.</summary>
    public DateTimeOffset? DestroyedAt { get; set; }
}
