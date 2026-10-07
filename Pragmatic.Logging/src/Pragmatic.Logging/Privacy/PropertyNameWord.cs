namespace Pragmatic.Logging.Privacy;

/// <summary>
///     Builds a property-name pattern that matches a sensitive term as a word of the name, not as a run of
///     letters inside another word.
/// </summary>
/// <remarks>
///     <para>
///         The shipped patterns used to be unanchored substrings, compiled case-insensitively: <c>.*ssn.*</c>
///         masked "ProcessName", <c>.*pin.*</c> "Shipping", <c>.*ip.*</c> "Description". A word here is what
///         a property name is made of: <c>CustomerSsn</c>, <c>customerSsn</c>, <c>CUSTOMER_SSN</c>,
///         <c>customer-ssn</c> and <c>APIKey</c> (an acronym followed by a word) all contain the word.
///     </para>
///     <para>
///         The pattern switches case sensitivity off with <c>(?-i)</c>, because the redactor compiles every
///         pattern with <c>IgnoreCase</c> and the word boundaries are where the case changes. The term is
///         accepted lowercase, capitalised or in capitals, with an optional plural <c>s</c>.
///     </para>
///     <para>
///         ⚠️ A name written all in lowercase is one word to this rule: <c>apikey</c> does not contain the
///         word "key". Short terms that need that case are paired with an explicit substring pattern for the
///         compound (<c>api_?key</c>), and long, unambiguous terms (password, secret, token) stay substrings.
///     </para>
/// </remarks>
internal static class PropertyNameWord
{
    // A word starts at the beginning, after a non-letter, at a lower→upper change ("customerSsn"), or at the
    // last capital of an acronym followed by lowercase ("APIKey").
    private const string WordStart = "(?:^|(?<=[^A-Za-z])|(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z]))";

    // A word ends where no lowercase letter follows.
    private const string WordEnd = "(?![a-z])";

    /// <summary>The pattern matching <paramref name="term" /> (lowercase letters) as a word of a property name.</summary>
    public static string Pattern(string term)
    {
        var capitalised = char.ToUpperInvariant(term[0]) + term[1..];
        var capitals = term.ToUpperInvariant();
        return $"(?-i){WordStart}(?:{term}|{capitalised}|{capitals})(?:s|S)?{WordEnd}";
    }
}
