namespace Pragmatic.Temporal.Types;

public readonly partial struct Period
{
    #region Operators

    /// <summary>
    ///     Adds two periods together.
    /// </summary>
    public static Period operator +(Period left, Period right)
    {
        return new Period(
            left.Years + right.Years,
            left.Months + right.Months,
            left.Days + right.Days);
    }

    /// <summary>
    ///     Subtracts one period from another.
    /// </summary>
    public static Period operator -(Period left, Period right)
    {
        return new Period(
            left.Years - right.Years,
            left.Months - right.Months,
            left.Days - right.Days);
    }

    /// <summary>
    ///     Negates all components of the period.
    /// </summary>
    public static Period operator -(Period period)
    {
        return period.Negate();
    }

    /// <summary>
    ///     Multiplies all components by a scalar.
    /// </summary>
    public static Period operator *(Period period, int scalar)
    {
        return new Period(
            period.Years * scalar,
            period.Months * scalar,
            period.Days * scalar);
    }

    /// <summary>
    ///     Multiplies all components by a scalar.
    /// </summary>
    public static Period operator *(int scalar, Period period) => period * scalar;

    public static bool operator ==(Period left, Period right) => left.Equals(right);

    public static bool operator !=(Period left, Period right) => !left.Equals(right);

    #endregion
}
