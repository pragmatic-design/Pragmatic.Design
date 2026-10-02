namespace Pragmatic.Temporal.Types;

public readonly partial struct LocalDateTime
{
    #region Operators

    public static bool operator ==(LocalDateTime left, LocalDateTime right)
    {
        return left._value == right._value;
    }

    public static bool operator !=(LocalDateTime left, LocalDateTime right)
    {
        return left._value != right._value;
    }

    public static bool operator <(LocalDateTime left, LocalDateTime right)
    {
        return left._value < right._value;
    }

    public static bool operator <=(LocalDateTime left, LocalDateTime right)
    {
        return left._value <= right._value;
    }

    public static bool operator >(LocalDateTime left, LocalDateTime right)
    {
        return left._value > right._value;
    }

    public static bool operator >=(LocalDateTime left, LocalDateTime right)
    {
        return left._value >= right._value;
    }

    public static LocalDateTime operator +(LocalDateTime dateTime, Duration duration)
    {
        return dateTime.Add(duration);
    }

    public static LocalDateTime operator -(LocalDateTime dateTime, Duration duration)
    {
        return dateTime.Add(-duration);
    }

    public static Duration operator -(LocalDateTime left, LocalDateTime right)
    {
        return right.DurationUntil(left);
    }

    #endregion
}
