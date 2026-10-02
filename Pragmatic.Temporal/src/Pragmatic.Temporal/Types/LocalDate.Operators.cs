namespace Pragmatic.Temporal.Types;

public readonly partial struct LocalDate
{
    #region Operators

    public static bool operator ==(LocalDate left, LocalDate right)
    {
        return left._value == right._value;
    }

    public static bool operator !=(LocalDate left, LocalDate right)
    {
        return left._value != right._value;
    }

    public static bool operator <(LocalDate left, LocalDate right)
    {
        return left._value < right._value;
    }

    public static bool operator <=(LocalDate left, LocalDate right)
    {
        return left._value <= right._value;
    }

    public static bool operator >(LocalDate left, LocalDate right)
    {
        return left._value > right._value;
    }

    public static bool operator >=(LocalDate left, LocalDate right)
    {
        return left._value >= right._value;
    }

    public static implicit operator DateOnly(LocalDate date)
    {
        return date._value;
    }

    public static implicit operator LocalDate(DateOnly dateOnly)
    {
        return new LocalDate(dateOnly);
    }

    #endregion
}
