using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     A composite key's values as one dictionary key: equal when every value is (boxed values compared with <see cref="object.Equals(object, object)" />,
///     the same CLR type on both sides: the parent's key and the child's foreign key are read with the same column kinds). What the runtime path groups
///     a composite parent's children on, built by generated code (no reflection, no generic tuple made at runtime).
/// </summary>
public readonly struct SqlCompositeKey : IEquatable<SqlCompositeKey>
{
    private readonly object?[] _values;

    public SqlCompositeKey(params object?[] values) => _values = values;

    public IReadOnlyList<object?> Values => _values;

    public bool Equals(SqlCompositeKey other)
    {
        if (_values.Length != other._values.Length)
            return false;
        for (var i = 0; i < _values.Length; i++)
        {
            if (!Equals(_values[i], other._values[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is SqlCompositeKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in _values)
            hash.Add(value);
        return hash.ToHashCode();
    }

    public override string ToString() => "(" + string.Join(", ", _values) + ")";
}
