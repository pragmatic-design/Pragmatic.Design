namespace Pragmatic.Internationalization.Types;

/// <summary>
///     Object-equality members of <see cref="LocalizedString"/>: <c>Equals(object)</c>,
///     <c>GetHashCode</c>, and the equality operators.
/// </summary>
public sealed partial class LocalizedString
{
    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LocalizedString other && Equals(other);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Hash code is computed by XOR-combining per-entry hashes (order-independent, no allocations).
    ///     As documented on the class, do NOT use <see cref="LocalizedString"/> as a dictionary key —
    ///     mutations via <see cref="Set"/>, <see cref="Remove"/>, or <see cref="Clear"/> will change
    ///     the hash code and corrupt any hash-based collection.
    /// </remarks>
    public override int GetHashCode()
    {
        // XOR is commutative so the result is order-independent and allocation-free.
        var combined = 0;
        foreach (var kvp in _values)
        {
            var entryHash = HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(kvp.Key),
                kvp.Value?.GetHashCode() ?? 0);
            combined ^= entryHash;
        }
        return combined;
    }

    /// <summary>
    ///     Equality operator.
    /// </summary>
    public static bool operator ==(LocalizedString? left, LocalizedString? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>
    ///     Inequality operator.
    /// </summary>
    public static bool operator !=(LocalizedString? left, LocalizedString? right)
    {
        return !(left == right);
    }
}
