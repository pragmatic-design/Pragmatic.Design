namespace Pragmatic.Privacy;

/// <summary>
///     What kind of personal data a property holds.
/// </summary>
/// <remarks>
///     The categories are not decoration: they are what the processing register reports, and
///     <see cref="Special" /> changes which diagnostics apply.
/// </remarks>
public enum DataCategory
{
    /// <summary>Names, identifiers, anything that says who someone is.</summary>
    Identity = 0,

    /// <summary>Email, phone, address.</summary>
    Contact = 1,

    /// <summary>Payment details, income, account balances.</summary>
    Financial = 2,

    /// <summary>Where someone is or has been.</summary>
    Location = 3,

    /// <summary>What someone did — activity, preferences, profiling output.</summary>
    Behavioural = 4,

    /// <summary>
    ///     Special categories: health, biometrics, ethnicity, political or religious belief, sexual
    ///     orientation, trade union membership.
    /// </summary>
    /// <remarks>
    ///     Kept apart from the others because the rules around it differ, not because it is "more
    ///     sensitive" in a vague sense. Exposing it through an endpoint with no authorization policy is
    ///     a diagnostic of its own.
    /// </remarks>
    Special = 5
}
