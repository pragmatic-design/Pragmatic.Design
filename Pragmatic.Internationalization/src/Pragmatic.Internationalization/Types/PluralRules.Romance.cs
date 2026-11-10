namespace Pragmatic.Internationalization.Types;

public static partial class PluralRules
{
    /// <summary>
    ///     French and Portuguese (CLDR's <c>pt</c>, Brazil's): one (0, 1), many (a whole number of millions), other.
    /// </summary>
    private static PluralCategory GetFrenchCategory(int n)
    {
        if (n is 0 or 1)
            return PluralCategory.One;
        return IsWholeMillions(n) ? PluralCategory.Many : PluralCategory.Other;
    }

    /// <summary>
    ///     Italian, Spanish, Catalan and European Portuguese (CLDR's <c>pt_PT</c>): one (1), many (a whole number of millions), other.
    /// </summary>
    /// <remarks>Unlike French, 0 is other: "0 ticket sono ancora aperti".</remarks>
    private static PluralCategory GetItalianCategory(int n)
    {
        if (n == 1)
            return PluralCategory.One;
        return IsWholeMillions(n) ? PluralCategory.Many : PluralCategory.Other;
    }

    /// <summary>CLDR's <c>e = 0 and i != 0 and i % 1000000 = 0</c> for a whole number: "un milione di", "un million de".</summary>
    private static bool IsWholeMillions(int n) => n != 0 && n % 1_000_000 == 0;

    /// <summary>
    ///     Romanian: one (1), few (0, and every other number whose last two digits are 1–19: 2, 19, 101, 1001), other
    /// </summary>
    private static PluralCategory GetRomanianCategory(int n)
    {
        if (n == 1)
            return PluralCategory.One;

        var mod100 = n % 100;
        if (n == 0 || mod100 is >= 1 and <= 19)
            return PluralCategory.Few;

        return PluralCategory.Other;
    }
}
