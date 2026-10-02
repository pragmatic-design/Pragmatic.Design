namespace Pragmatic.Privacy.Tests.Wiring;

/// <summary>
///     A consent store with nothing in it — <see cref="SubjectAccessService" /> asks for one, and
///     consent is not what this suite is about.
/// </summary>
public sealed class EmptyConsentStore : IConsentStore
{
    /// <inheritdoc />
    public ValueTask GrantAsync(
        string subjectRef, string purpose, string noticeVersion, string? source = null,
        CancellationToken ct = default) => default;

    /// <inheritdoc />
    public ValueTask<bool> WithdrawAsync(
        string subjectRef, string purpose, DateTimeOffset now, CancellationToken ct = default) => new(false);

    /// <inheritdoc />
    public ValueTask<bool> IsGrantedAsync(
        string subjectRef, string purpose, string noticeVersion, CancellationToken ct = default) => new(false);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ConsentRecord>> GetHistoryAsync(
        string subjectRef, CancellationToken ct = default) => new([]);
}
