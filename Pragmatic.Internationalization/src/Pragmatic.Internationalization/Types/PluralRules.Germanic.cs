namespace Pragmatic.Internationalization.Types;

public static partial class PluralRules
{
    /// <summary>
    ///     Germanic languages (English, German, Dutch, etc.): one, other
    /// </summary>
    private static PluralCategory GetGermanicCategory(int n)
    {
        return n == 1 ? PluralCategory.One : PluralCategory.Other;
    }
}