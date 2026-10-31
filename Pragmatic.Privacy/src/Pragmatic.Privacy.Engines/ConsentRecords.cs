namespace Pragmatic.Privacy.Engines;

/// <summary>A stored consent read back as the contract's <see cref="ConsentRecord" />, on either engine.</summary>
internal static class ConsentRecords
{
    public static ConsentRecord ToRecord<T>(T row) where T : IConsentRow => new()
    {
        SubjectRef = row.SubjectRef,
        Purpose = row.Purpose,
        NoticeVersion = row.NoticeVersion,
        GrantedAt = new DateTimeOffset(row.GrantedAt, TimeSpan.Zero),
        WithdrawnAt = row.WithdrawnAt is { } withdrawn ? new DateTimeOffset(withdrawn, TimeSpan.Zero) : null,
        Source = row.Source,
    };
}
