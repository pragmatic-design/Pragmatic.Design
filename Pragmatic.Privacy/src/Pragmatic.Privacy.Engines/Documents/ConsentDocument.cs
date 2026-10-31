using Pragmatic.Persistence.Documents.Attributes;
using Pragmatic.Persistence.Documents.Schema;

namespace Pragmatic.Privacy.Engines.Documents;

/// <summary>
///     A consent on the documents engine (collection <c>__Consents</c>): <see cref="Sql.ConsentRow" />'s fields, its id the three parts of that
///     row's key (<see cref="IdOf" />), so a consent under one notice version is one document.
/// </summary>
[Document("__Consents")]
public sealed partial class ConsentDocument : IConsentRow
{
    /// <summary>The collection in a boundary's <c>{Boundary}DocSchema</c>, with the index "what has this subject agreed to" reads.</summary>
    public static readonly DocCollectionSchema Schema = new("__Consents", "/id", [new DocIndex("IX___Consents_SubjectRef", ["SubjectRef"])], []);

    [Key]
    public string Id { get; set; } = "";

    public string SubjectRef { get; set; } = "";

    public string Purpose { get; set; } = "";

    public string NoticeVersion { get; set; } = "";

    /// <summary>When it was given, in UTC ticks.</summary>
    public long GrantedAt { get; set; }

    /// <summary>When it was withdrawn, in UTC ticks; null while it stands.</summary>
    public long? WithdrawnAt { get; set; }

    public string? Source { get; set; }

    /// <summary>
    ///     The id of the consent of <paramref name="subjectRef" /> to <paramref name="purpose" /> under <paramref name="noticeVersion" />: each part
    ///     prefixed by its length, so no two keys write the same id by moving a character from one part to the next.
    /// </summary>
    public static string IdOf(string subjectRef, string purpose, string noticeVersion)
        => $"{subjectRef.Length}:{subjectRef}|{purpose.Length}:{purpose}|{noticeVersion.Length}:{noticeVersion}";
}
