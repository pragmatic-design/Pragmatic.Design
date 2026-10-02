namespace Pragmatic.Privacy;

/// <summary>
///     What one erasure step did.
/// </summary>
/// <param name="ErasedCount">How many records the step erased.</param>
/// <param name="Retained">
///     What it deliberately kept, and why. Empty when nothing was retained.
/// </param>
public readonly record struct ErasureStepResult(int ErasedCount, IReadOnlyList<RetainedItem> Retained)
{
    /// <summary>A step that erased everything it found.</summary>
    public static ErasureStepResult Erased(int count) => new(count, []);

    /// <summary>A step with nothing to do.</summary>
    public static ErasureStepResult Nothing { get; } = new(0, []);
}
