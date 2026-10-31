namespace Pragmatic.Privacy.Engines;

/// <summary>A consent as both engines store it, its instants UTC ticks: what <see cref="ConsentRecords" /> reads back as a <see cref="ConsentRecord" />.</summary>
public interface IConsentRow
{
    string SubjectRef { get; }

    string Purpose { get; }

    string NoticeVersion { get; }

    long GrantedAt { get; }

    long? WithdrawnAt { get; }

    string? Source { get; }
}
