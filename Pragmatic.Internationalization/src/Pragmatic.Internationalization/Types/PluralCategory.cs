namespace Pragmatic.Internationalization.Types;

/// <summary>
///     CLDR plural categories as defined by Unicode CLDR.
///     Different languages use different subsets of these categories.
/// </summary>
public enum PluralCategory
{
    /// <summary>
    ///     Zero form (e.g., Arabic "0 رسائل").
    /// </summary>
    Zero,

    /// <summary>
    ///     Singular form (e.g., English "1 item").
    /// </summary>
    One,

    /// <summary>
    ///     Dual form (e.g., Arabic "رسالتان" for 2).
    /// </summary>
    Two,

    /// <summary>
    ///     Paucal/few form (e.g., Russian "2 элемента" for 2-4).
    /// </summary>
    Few,

    /// <summary>
    ///     Many form (e.g., Russian "5 элементов" for 5-20).
    /// </summary>
    Many,

    /// <summary>
    ///     General plural form - used when no other category applies.
    ///     All languages have this category.
    /// </summary>
    Other
}