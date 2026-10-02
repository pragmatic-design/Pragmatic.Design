namespace Pragmatic.Internationalization.Types;

public static partial class PluralRules
{
    /// <summary>
    ///     East Slavic languages (Russian, Ukrainian, Belarusian)
    ///     one: ends in 1, not 11 (1, 21, 31, ...)
    ///     few: ends in 2-4, not 12-14 (2, 3, 4, 22, 23, 24, ...)
    ///     many: everything else (0, 5-20, 25-30, ...)
    /// </summary>
    private static PluralCategory GetEastSlavicCategory(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;

        if (mod10 == 1 && mod100 != 11)
            return PluralCategory.One;
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return PluralCategory.Few;
        return PluralCategory.Many;
    }

    /// <summary>
    ///     Polish: similar to East Slavic but with different ranges
    ///     one: 1
    ///     few: ends in 2-4, not 12-14
    ///     many: everything else
    /// </summary>
    private static PluralCategory GetPolishCategory(int n)
    {
        if (n == 1)
            return PluralCategory.One;

        var mod10 = n % 10;
        var mod100 = n % 100;

        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return PluralCategory.Few;
        return PluralCategory.Many;
    }

    /// <summary>
    ///     Czech/Slovak: one, few (2-4), other
    /// </summary>
    private static PluralCategory GetCzechSlovakCategory(int n)
    {
        return n switch
        {
            1 => PluralCategory.One,
            >= 2 and <= 4 => PluralCategory.Few,
            _ => PluralCategory.Other
        };
    }

    /// <summary>
    ///     Serbian/Croatian/Bosnian: similar to Russian
    /// </summary>
    private static PluralCategory GetSerboCroatianCategory(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;

        if (mod10 == 1 && mod100 != 11)
            return PluralCategory.One;
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return PluralCategory.Few;
        return PluralCategory.Other;
    }

    /// <summary>
    ///     Slovenian: one (1, 101, 201...), two (2, 102, 202...), few (3-4, 103-104...), other
    /// </summary>
    private static PluralCategory GetSlovenianCategory(int n)
    {
        var mod100 = n % 100;

        return mod100 switch
        {
            1 => PluralCategory.One,
            2 => PluralCategory.Two,
            3 or 4 => PluralCategory.Few,
            _ => PluralCategory.Other
        };
    }
}