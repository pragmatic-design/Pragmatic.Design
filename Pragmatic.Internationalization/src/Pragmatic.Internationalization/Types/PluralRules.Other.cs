namespace Pragmatic.Internationalization.Types;

public static partial class PluralRules
{
    /// <summary>
    ///     Arabic: zero, one, two, few (3-10), many (11-99), other
    /// </summary>
    private static PluralCategory GetArabicCategory(int n)
    {
        var mod100 = n % 100;

        return n switch
        {
            0 => PluralCategory.Zero,
            1 => PluralCategory.One,
            2 => PluralCategory.Two,
            _ when mod100 is >= 3 and <= 10 => PluralCategory.Few,
            _ when mod100 is >= 11 and <= 99 => PluralCategory.Many,
            _ => PluralCategory.Other
        };
    }

    /// <summary>
    ///     Welsh: zero, one, two, few (3), many (6), other
    /// </summary>
    private static PluralCategory GetWelshCategory(int n)
    {
        return n switch
        {
            0 => PluralCategory.Zero,
            1 => PluralCategory.One,
            2 => PluralCategory.Two,
            3 => PluralCategory.Few,
            6 => PluralCategory.Many,
            _ => PluralCategory.Other
        };
    }

    /// <summary>
    ///     Irish: one (1), two (2), few (3-6), many (7-10), other
    /// </summary>
    private static PluralCategory GetIrishCategory(int n)
    {
        return n switch
        {
            1 => PluralCategory.One,
            2 => PluralCategory.Two,
            >= 3 and <= 6 => PluralCategory.Few,
            >= 7 and <= 10 => PluralCategory.Many,
            _ => PluralCategory.Other
        };
    }

    /// <summary>
    ///     Latvian: zero, one (ends in 1 but not 11), other
    /// </summary>
    private static PluralCategory GetLatvianCategory(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;

        if (n == 0)
            return PluralCategory.Zero;
        if (mod10 == 1 && mod100 != 11)
            return PluralCategory.One;
        return PluralCategory.Other;
    }

    /// <summary>
    ///     Lithuanian: one (ends in 1, not 11), few (ends in 2-9, not 12-19), other
    /// </summary>
    private static PluralCategory GetLithuanianCategory(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;

        if (mod10 == 1 && mod100 != 11)
            return PluralCategory.One;
        if (mod10 is >= 2 and <= 9 && mod100 is < 12 or > 19)
            return PluralCategory.Few;
        return PluralCategory.Other;
    }

    /// <summary>
    ///     Hebrew: one (1), two (2), other
    /// </summary>
    private static PluralCategory GetHebrewCategory(int n)
    {
        return n switch
        {
            1 => PluralCategory.One,
            2 => PluralCategory.Two,
            _ => PluralCategory.Other
        };
    }

    /// <summary>
    ///     Indic languages (Hindi, Bengali, etc.): one (0-1), other
    /// </summary>
    private static PluralCategory GetIndicCategory(int n)
    {
        return n is 0 or 1 ? PluralCategory.One : PluralCategory.Other;
    }
}