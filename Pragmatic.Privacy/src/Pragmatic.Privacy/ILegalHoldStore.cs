namespace Pragmatic.Privacy;

/// <summary>
///     Answers whether a subject's data is currently frozen by a legal hold.
/// </summary>
/// <remarks>
///     A hold is distinct from a retention rule, and the two are not interchangeable. A retention rule is
///     <b>structural and permanent</b> — a fiscal obligation on an invoice line, declared next to the
///     field and visible in the processing register. A hold is <b>exceptional and temporary</b> — a live
///     dispute suspends the right to erasure until it ends. Neither can be expressed as the other:
///     encoding a lawsuit in an attribute would be absurd, and a runtime lookup cannot tell the register
///     what is permanently retained.
/// </remarks>
public interface ILegalHoldStore
{
    /// <summary>Returns the active holds over this subject, empty when none apply.</summary>
    ValueTask<IReadOnlyList<RetainedItem>> GetActiveHoldsAsync(
        string subjectRef, CancellationToken ct = default);
}
