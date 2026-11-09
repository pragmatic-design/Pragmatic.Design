using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value comparer for a <see cref="LocalizedString" /> column: compares and hashes by culture and text, and snapshots a copy.
/// </summary>
/// <remarks>
///     <see cref="LocalizedString.Set" />, <see cref="LocalizedString.Remove" /> and <see cref="LocalizedString.Clear" /> change the instance
///     in place. EF Core detects a change by comparing the current value with the snapshot it took at load; with the instance itself as the
///     snapshot the two are always equal, and a change made in place is never saved. The copy is what makes it visible.
/// </remarks>
public sealed class LocalizedStringValueComparer : ValueComparer<LocalizedString>
{
    /// <summary>
    ///     Creates a new instance of the comparer.
    /// </summary>
    public LocalizedStringValueComparer()
        : base(
            (left, right) => left == right,
            value => value == null ? 0 : value.GetHashCode(),
            value => value == null ? null! : LocalizedString.From(new Dictionary<string, string>(value)))
    {
    }
}
