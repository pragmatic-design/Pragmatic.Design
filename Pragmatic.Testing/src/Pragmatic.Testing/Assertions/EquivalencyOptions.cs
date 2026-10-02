namespace Pragmatic.Testing.Assertions;

/// <summary>
///     How <c>BeEquivalentTo</c> should compare — currently only whether order matters.
/// </summary>
/// <remarks>
///     Three call sites in this repository configure anything at all, which is why this carries one
///     option rather than the dozens the library it replaces offers. Add one when a caller needs it,
///     not before.
/// </remarks>
public sealed class EquivalencyOptions
{
    /// <summary>Whether the elements must appear in the same order.</summary>
    internal bool StrictOrdering { get; private set; }

    /// <summary>Requires the elements to be in the same order, not merely the same.</summary>
    public EquivalencyOptions WithStrictOrdering()
    {
        StrictOrdering = true;
        return this;
    }

    /// <summary>Allows the elements to appear in any order. The default.</summary>
    public EquivalencyOptions WithoutStrictOrdering()
    {
        StrictOrdering = false;
        return this;
    }
}
