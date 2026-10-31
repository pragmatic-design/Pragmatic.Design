using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Privacy.Engines.Sql;

/// <summary>
///     A subject of the registry on the Sql engine (<c>__Subjects</c>): EF Core's table, column for column, its instants UTC ticks, so a
///     database holds one registry whichever engine writes it. The blind index is unique among the subjects that have one (the schema's index).
/// </summary>
[Entity]
[Table("__Subjects")]
public sealed class SubjectRow
{
    /// <summary>The opaque reference. Random, never derived from the identity.</summary>
    [Key]
    [MaxLength(64)]
    public string SubjectRef { get; set; } = "";

    [MaxLength(128)]
    public string SubjectType { get; set; } = "";

    /// <summary>The keyed hash of (type, identity), of its fixed length (an index can hold it); null once the subject is forgotten.</summary>
    [MaxLength(SubjectLookup.BlindIndexLength)]
    public byte[]? LookupIndex { get; set; }

    /// <summary>The encrypted identity; null once the subject is forgotten.</summary>
    public byte[]? Identifier { get; set; }

    /// <summary>When the reference was allocated, in UTC ticks.</summary>
    public long CreatedAt { get; set; }

    /// <summary>When the link was broken, in UTC ticks; null while the subject is known.</summary>
    public long? ForgottenAt { get; set; }
}
