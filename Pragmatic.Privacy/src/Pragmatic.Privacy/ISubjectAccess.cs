namespace Pragmatic.Privacy;

/// <summary>
///     Assembles everything held about a subject, for an access or a portability request.
/// </summary>
/// <remarks>
///     The interface exists so an operation can depend on access: the generator does not inject a
///     concrete type, because it cannot tell an injected service from plain state (<c>PRAG0419</c>).
///     Implemented by <see cref="SubjectAccessService" />.
/// </remarks>
public interface ISubjectAccess
{
    /// <inheritdoc cref="SubjectAccessService.CollectAsync" />
    ValueTask<SubjectDataExport> CollectAsync(string subjectRef, CancellationToken ct = default);
}
