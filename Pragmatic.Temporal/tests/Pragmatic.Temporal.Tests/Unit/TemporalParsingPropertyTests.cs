using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     The text parsing of the temporal types a route or a query parameter binds, through <see cref="IParsable{TSelf}" />
///     as the binder calls it (#158, #311). For any text <c>TryParse</c> answers true or false and <c>Parse</c> returns
///     or throws <see cref="FormatException" />, the two agreeing; nothing else escapes.
/// </summary>
public class TemporalParsingPropertyTests
{
    // What the formats are made of and what sits next to them: ASCII digits, digits of other scripts that a \d or
    // char.IsDigit accepts, runs too long for an int or a long, signs, separators and every designator.
    private static readonly string[] Pieces =
    [
        "0", "1", "9", "12", "2024", "٣", "５", "2147483648", "99999999999999999999",
        "-", "+", ":", ".", ",", "/", " ", "T", "Z", "P", "Y", "M", "W", "D", "H", "S", "e",
    ];

    // Numbers and designators in turn after a P, which is the shape both ISO 8601 parsers match before they read a
    // number: a run of pieces alone reaches that shape too rarely to test what happens to the number.
    private static readonly Gen<string> Designated =
        from sign in Gen.Elements("", "-")
        from parts in Gen.Elements(Pieces[..9]).Zip(Gen.Elements("Y", "M", "W", "D", "H", "S", "T", "TH", ".5S"))
            .Select(pair => pair.Item1 + pair.Item2).NonEmptyListOf()
        select $"{sign}P{string.Concat(parts)}";

    private static readonly Arbitrary<string> Texts =
        Gen.Frequency(
                (3, Gen.Elements(Pieces).ListOf().Select(pieces => string.Concat(pieces))),
                (2, Designated),
                (1, ArbMap.Default.ArbFor<string>().Generator))
            .ToArbitrary();

    [Fact]
    public void LocalDate_text_parses_to_a_declared_outcome() => AnyTextIsDeclared<LocalDate>();

    [Fact]
    public void LocalTime_text_parses_to_a_declared_outcome() => AnyTextIsDeclared<LocalTime>();

    [Fact]
    public void LocalDateTime_text_parses_to_a_declared_outcome() => AnyTextIsDeclared<LocalDateTime>();

    [Fact]
    public void Duration_text_parses_to_a_declared_outcome() => AnyTextIsDeclared<Duration>();

    [Fact]
    public void Period_text_parses_to_a_declared_outcome() => AnyTextIsDeclared<Period>();

    /// <summary>
    ///     A period written in the format is read as its components when each fits an <see cref="int" />, and refused
    ///     when one does not.
    /// </summary>
    [Fact]
    public void A_period_is_read_exactly_or_refused_when_a_component_overflows()
    {
        var component = Gen.Frequency(
            (4, Gen.Choose(-100_000, 100_000).Select(n => (BigInteger?)n)),
            (1, Gen.Elements<BigInteger?>(int.MaxValue, int.MinValue, (BigInteger)int.MaxValue + 1,
                (BigInteger)int.MinValue - 1, BigInteger.Parse("99999999999999999999"))),
            (2, Gen.Constant<BigInteger?>(null)));

        Prop.ForAll(component.ToArbitrary(), component.ToArbitrary(), component.ToArbitrary(), (years, months, days) =>
        {
            if (years is null && months is null && days is null)
                return true;

            var text = $"P{WithDesignator(years, "Y")}{WithDesignator(months, "M")}{WithDesignator(days, "D")}";
            var fits = new[] { years, months, days }.All(c => c is null || (c >= int.MinValue && c <= int.MaxValue));

            var parsed = Period.TryParse(text, out var period);
            return fits
                ? parsed && period == new Period((int)(years ?? 0), (int)(months ?? 0), (int)(days ?? 0))
                : !parsed;
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(500));
    }

    /// <summary>
    ///     A duration of whole units is read as its exact length when it fits a <see cref="TimeSpan" />, and refused
    ///     when it does not: never a length that wrapped or was clamped.
    /// </summary>
    [Fact]
    public void A_duration_is_read_exactly_or_refused_when_it_overflows()
    {
        // Small enough that the parser's double arithmetic is exact, or far past what a TimeSpan holds: the band in
        // between is where a double cannot say which side of the bound a value falls on.
        var component = Gen.Frequency(
            (4, Gen.Choose(0, 100_000).Select(n => (BigInteger?)n)),
            (1, Gen.Elements<BigInteger?>(BigInteger.Parse("1000000000000000"), BigInteger.Parse("99999999999999999999"))),
            (2, Gen.Constant<BigInteger?>(null)));

        var components =
            from d in component
            from h in component
            from m in component
            from s in component
            select (d, h, m, s);

        Prop.ForAll(components.ToArbitrary(), c =>
            {
                var (days, hours, minutes, seconds) = c;
                if (days is null && hours is null && minutes is null && seconds is null)
                    return true;

                var time = $"{WithDesignator(hours, "H")}{WithDesignator(minutes, "M")}{WithDesignator(seconds, "S")}";
                var text = $"P{WithDesignator(days, "D")}{(time.Length > 0 ? "T" + time : "")}";
                var ticks = (days ?? 0) * TimeSpan.TicksPerDay + (hours ?? 0) * TimeSpan.TicksPerHour
                            + (minutes ?? 0) * TimeSpan.TicksPerMinute + (seconds ?? 0) * TimeSpan.TicksPerSecond;

                var parsed = Duration.TryParse(text, out var duration);
                return ticks <= long.MaxValue
                    ? parsed && duration.ToTimeSpan().Ticks == (long)ticks
                    : !parsed;
            }).Check(Config.QuickThrowOnFailure.WithMaxTest(500));
    }

    private static string WithDesignator(BigInteger? component, string designator)
        => component is { } value ? value + designator : "";

    private static void AnyTextIsDeclared<T>() where T : IParsable<T>
        => Prop.ForAll(Texts, text =>
        {
            var parsed = T.TryParse(text, null, out _);
            try
            {
                T.Parse(text, null);
                return parsed;
            }
            catch (FormatException)
            {
                return !parsed;
            }
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(1000));
}
