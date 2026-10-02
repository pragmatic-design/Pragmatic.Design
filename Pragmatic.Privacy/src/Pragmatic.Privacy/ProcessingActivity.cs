namespace Pragmatic.Privacy;

/// <summary>
///     One processing activity as the Article 30 register describes it.
/// </summary>
/// <param name="EntityType">The type holding the data.</param>
/// <param name="Categories">The categories of personal data processed there.</param>
/// <param name="IsDataSubject">Whether this type is itself a data subject.</param>
/// <param name="Erasure">How each field is erased, keyed by field name.</param>
/// <param name="Retained">Fields deliberately kept, with the obligation behind each.</param>
/// <param name="Purpose">
///     Why the processing happens. <see langword="null" /> when the code alone cannot say — which is
///     most of the time, and is the half the controller has to declare.
/// </param>
public sealed record ProcessingActivity(
    string EntityType,
    IReadOnlyList<string> Categories,
    bool IsDataSubject,
    IReadOnlyDictionary<string, string> Erasure,
    IReadOnlyList<RetainedItem> Retained,
    string? Purpose = null)
{
    /// <summary>
    ///     True when the entry still needs a purpose before the register is complete.
    /// </summary>
    /// <remarks>
    ///     Reported rather than filled in with something plausible. A register that invents its own
    ///     purposes reads as authoritative and is not, which is worse than one that says what is missing.
    /// </remarks>
    public bool NeedsDeclaredPurpose => string.IsNullOrWhiteSpace(Purpose);
}
