using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Privacy.Engines.Sql;

/// <summary>
///     A consent on the Sql engine (<c>__Consents</c>): EF Core's table, column for column. Keyed on the notice version too: consenting again
///     under a new notice is a new fact, not an update of the old one.
/// </summary>
[Entity]
[Table("__Consents")]
public sealed class ConsentRow : IConsentRow
{
    [Key]
    [MaxLength(64)]
    public string SubjectRef { get; set; } = "";

    [Key]
    [MaxLength(256)]
    public string Purpose { get; set; } = "";

    [Key]
    [MaxLength(64)]
    public string NoticeVersion { get; set; } = "";

    /// <summary>When it was given, in UTC ticks.</summary>
    public long GrantedAt { get; set; }

    /// <summary>When it was withdrawn, in UTC ticks; null while it stands.</summary>
    public long? WithdrawnAt { get; set; }

    [MaxLength(256)]
    public string? Source { get; set; }
}
