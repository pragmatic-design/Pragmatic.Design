namespace TimeOff.Leave.Infrastructure.Internationalization;

/// <summary>
///     The languages Time off speaks: English, the default, and Italian.
/// </summary>
/// <remarks>
///     One list for both readers: the host declares these to the culture middleware, and a preference
///     stored on the profile is checked against them — a language the host does not serve would be a
///     choice that silently changes nothing.
/// </remarks>
public static class Languages
{
    public static readonly CultureCode English = CultureCode.EnglishUS;

    public static readonly CultureCode Italian = CultureCode.FromString("it-IT");

    public static IReadOnlyList<CultureCode> Supported { get; } = [English, Italian];

    /// <summary>The supported languages as a reader writes them: <c>en-US, it-IT</c>.</summary>
    public static string Listed { get; } = string.Join(", ", Supported.Select(c => c.Code));

    public static bool IsSupported(string language)
        => CultureCode.TryFromString(language, out var culture) && Supported.Contains(culture);
}
