namespace Pragmatic.Redaction;

/// <summary>
///     Replaces the personal-data shapes of <see cref="PersonalDataPatterns" /> with a mask.
/// </summary>
/// <remarks>
///     Order matters where patterns overlap: the labelled ones run before the broad digit run, so
///     <c>cvv: 1234</c> is masked as a verification value rather than partially swallowed.
/// </remarks>
public static class PersonalDataRedactor
{
    /// <summary>Redacts <paramref name="text" />, or returns it unchanged when there is nothing to do.</summary>
    public static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = PersonalDataPatterns.Email().Replace(text, PersonalDataPatterns.Mask);
        result = PersonalDataPatterns.BearerToken().Replace(result, $"Bearer {PersonalDataPatterns.Mask}");
        result = PersonalDataPatterns.CardVerificationValue().Replace(result, PersonalDataPatterns.Mask);
        result = PersonalDataPatterns.NationalIdentifier().Replace(result, PersonalDataPatterns.Mask);
        result = PersonalDataPatterns.LongDigitRun().Replace(result, PersonalDataPatterns.Mask);
        result = PersonalDataPatterns.Iban().Replace(result, PersonalDataPatterns.Mask);
        result = PersonalDataPatterns.KeyValueSecret().Replace(result, $"$1={PersonalDataPatterns.Mask}");

        return result;
    }
}
