namespace Pragmatic.Cryptography;

/// <summary>
///     Thrown when a subject's key is requested after it has been destroyed.
/// </summary>
/// <remarks>
///     Recreating the key would silently undo an erasure: data written afterwards would be readable
///     again under a reference already reported as erased. Failing loudly is the point — the caller must
///     decide, and the answer is normally a new subject reference.
/// </remarks>
public sealed class SubjectKeyDestroyedException(string subjectRef, DateTimeOffset destroyedAt)
    : InvalidOperationException(
        $"The encryption key for subject '{subjectRef}' was destroyed on {destroyedAt:O} and is never " +
        "recreated. Data encrypted under it is permanently unreadable by design. If this subject is " +
        "active again, allocate a new subject reference.")
{
    /// <summary>The subject whose key was destroyed.</summary>
    public string SubjectRef { get; } = subjectRef;

    /// <summary>When the key was destroyed.</summary>
    public DateTimeOffset DestroyedAt { get; } = destroyedAt;
}
