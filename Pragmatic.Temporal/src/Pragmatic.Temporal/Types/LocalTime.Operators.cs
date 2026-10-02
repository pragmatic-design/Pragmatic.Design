namespace Pragmatic.Temporal.Types;

public readonly partial struct LocalTime
{
    #region Operators

    public static bool operator ==(LocalTime left, LocalTime right)
    {
        return left._value == right._value;
    }

    public static bool operator !=(LocalTime left, LocalTime right)
    {
        return left._value != right._value;
    }

    public static bool operator <(LocalTime left, LocalTime right)
    {
        return left._value < right._value;
    }

    public static bool operator <=(LocalTime left, LocalTime right)
    {
        return left._value <= right._value;
    }

    public static bool operator >(LocalTime left, LocalTime right)
    {
        return left._value > right._value;
    }

    public static bool operator >=(LocalTime left, LocalTime right)
    {
        return left._value >= right._value;
    }

    public static LocalTime operator +(LocalTime time, TimeSpan duration)
    {
        return time.Add(duration);
    }

    public static LocalTime operator -(LocalTime time, TimeSpan duration)
    {
        return time.Add(-duration);
    }

    public static implicit operator TimeOnly(LocalTime time)
    {
        return time._value;
    }

    public static implicit operator LocalTime(TimeOnly timeOnly)
    {
        return new LocalTime(timeOnly);
    }

    #endregion
}
