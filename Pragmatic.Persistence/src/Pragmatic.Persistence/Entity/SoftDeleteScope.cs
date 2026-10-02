namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Steps out of soft delete for the work inside it, so a delete really deletes.
/// </summary>
/// <remarks>
///     <para>
///         Soft delete is enforced at save time, which is what makes it hold on every path — a
///         repository call, a mutation, a child leaving its parent's collection, a hand-written
///         <c>context.Remove</c>. That leaves erasure with no way to say what it means, and erasure is
///         the one caller that must be able to: an <c>IErasureStep</c> answering a subject's request
///         has to remove the row, not flag it, or the request is not honoured and nothing says so.
///     </para>
///     <para>
///         Explicit and scoped rather than a flag on the entity: the decision belongs to the operation
///         doing the erasing, not to the type. Everything else keeps the guarantee.
///     </para>
///     <example>
///         <code>
/// using (SoftDeleteScope.Suspend())
/// {
///     repository.Remove(subject);
///     await unitOfWork.SaveChangesAsync(ct);   // the row is gone
/// }
///         </code>
///     </example>
/// </remarks>
public static class SoftDeleteScope
{
    private static readonly AsyncLocal<bool> SuspendedFlag = new();

    /// <summary>
    ///     Whether the current asynchronous flow has stepped out of soft delete.
    /// </summary>
    public static bool IsSuspended => SuspendedFlag.Value;

    /// <summary>
    ///     Suspends soft delete until the returned value is disposed.
    /// </summary>
    /// <returns>A scope that restores the previous state.</returns>
    /// <remarks>
    ///     Restores rather than clears, so nesting behaves: an erasure step that calls another one
    ///     does not hand the guarantee back halfway through.
    /// </remarks>
    public static Suspension Suspend()
    {
        var previous = SuspendedFlag.Value;
        SuspendedFlag.Value = true;
        return new Suspension(previous);
    }

    /// <summary>Restores what <see cref="Suspend" /> stepped out of.</summary>
    public readonly struct Suspension(bool previous) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => SuspendedFlag.Value = previous;
    }
}
