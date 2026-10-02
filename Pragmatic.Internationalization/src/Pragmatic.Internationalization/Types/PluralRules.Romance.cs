namespace Pragmatic.Internationalization.Types;

public static partial class PluralRules
{
    /// <summary>
    ///     Romance languages (French, Italian, Spanish, etc.): one (0-1), other
    ///     French uses singular for 0 and 1.
    /// </summary>
    private static PluralCategory GetRomanceCategory(int n)
    {
        return n is 0 or 1 ? PluralCategory.One : PluralCategory.Other;
    }

    /// <summary>
    ///     Romanian: one (1), few (0, 2-19, 101-119, etc.), other
    /// </summary>
    private static PluralCategory GetRomanianCategory(int n)
    {
        if (n == 1)
            return PluralCategory.One;

        var mod100 = n % 100;
        if (n == 0 || mod100 is >= 2 and <= 19)
            return PluralCategory.Few;

        return PluralCategory.Other;
    }
}