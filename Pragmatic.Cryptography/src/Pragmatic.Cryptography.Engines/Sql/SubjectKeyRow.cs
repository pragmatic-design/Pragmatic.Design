using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Cryptography.Engines.Sql;

/// <summary>
///     A subject's key on the Sql engine (<c>__SubjectKeys</c>): EF Core's table, column for column, so a database holds one set of keys whichever
///     engine writes it. The key id is unique (the schema's index): reads resolve by it.
/// </summary>
/// <remarks>The row survives destruction without its key: the id is what tells an erased value from a tampered one.</remarks>
[Entity]
[Table("__SubjectKeys")]
public sealed class SubjectKeyRow
{
    /// <summary>Opaque reference to the subject: a pseudonym, never an identity.</summary>
    [Key]
    [MaxLength(128)]
    public string SubjectRef { get; set; } = "";

    /// <summary>The fingerprint of the key material, which a ciphertext header carries.</summary>
    [MaxLength(64)]
    public string KeyId { get; set; } = "";

    /// <summary>The key wrapped by the master key ring, the subject reference as associated data; null once destroyed.</summary>
    public byte[]? WrappedKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the key was destroyed; null while it is live.</summary>
    public DateTimeOffset? DestroyedAt { get; set; }
}
