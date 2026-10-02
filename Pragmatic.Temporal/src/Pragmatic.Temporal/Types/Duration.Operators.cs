namespace Pragmatic.Temporal.Types;

public readonly partial struct Duration
{
    #region Operators

    public static Duration operator +(Duration left, Duration right)
    {
        return new Duration(left._value + right._value);
    }

    public static Duration operator -(Duration left, Duration right)
    {
        return new Duration(left._value - right._value);
    }

    public static Duration operator *(Duration duration, double factor)
    {
        return new Duration(duration._value * factor);
    }

    public static Duration operator *(double factor, Duration duration)
    {
        return new Duration(duration._value * factor);
    }

    public static Duration operator /(Duration duration, double divisor)
    {
        if (divisor == 0)
            throw new DivideByZeroException("Cannot divide a Duration by zero.");
        return new Duration(duration._value / divisor);
    }

    public static double operator /(Duration left, Duration right)
    {
        return left._value / right._value;
    }

    public static Duration operator -(Duration duration)
    {
        return new Duration(-duration._value);
    }

    public static Duration operator +(Duration duration)
    {
        return duration;
    }

    public static bool operator ==(Duration left, Duration right)
    {
        return left._value == right._value;
    }

    public static bool operator !=(Duration left, Duration right)
    {
        return left._value != right._value;
    }

    public static bool operator <(Duration left, Duration right)
    {
        return left._value < right._value;
    }

    public static bool operator <=(Duration left, Duration right)
    {
        return left._value <= right._value;
    }

    public static bool operator >(Duration left, Duration right)
    {
        return left._value > right._value;
    }

    public static bool operator >=(Duration left, Duration right)
    {
        return left._value >= right._value;
    }

    /// <summary>Implicit conversion from Duration to TimeSpan.</summary>
    public static implicit operator TimeSpan(Duration duration)
    {
        return duration._value;
    }

    /// <summary>Explicit conversion from TimeSpan to Duration.</summary>
    public static explicit operator Duration(TimeSpan timeSpan)
    {
        return new Duration(timeSpan);
    }

    #endregion
}
