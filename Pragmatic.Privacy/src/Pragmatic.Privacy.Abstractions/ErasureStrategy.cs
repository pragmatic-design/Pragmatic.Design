namespace Pragmatic.Privacy;

/// <summary>
///     What happens to a property when its subject is erased.
/// </summary>
public enum ErasureStrategy
{
    /// <summary>Clear the value.</summary>
    Null = 0,

    /// <summary>Delete the whole row. Use when the record has no meaning without the person.</summary>
    Delete = 1,

    /// <summary>
    ///     Replace with a value that carries no identity but keeps the shape — so aggregates and
    ///     reports built on the column keep working.
    /// </summary>
    Anonymize = 2,

    /// <summary>
    ///     Replace with the subject's pseudonym. Keeps rows joinable to each other — "how many orders in
    ///     March" still answers — without a person behind them.
    /// </summary>
    Pseudonymize = 3,

    /// <summary>
    ///     Keep the value. <b>Requires a stated reason</b>, which the generator enforces and the
    ///     processing register reports.
    /// </summary>
    /// <remarks>
    ///     An obligation to retain is a legitimate answer to an erasure request — a fiscal record does
    ///     not disappear because someone asks. What is not legitimate is retaining silently: the reason
    ///     has to sit next to the field, so it appears in the register and in the outcome the subject is
    ///     told about.
    /// </remarks>
    Retain = 4,

    /// <summary>
    ///     Erase by destroying the subject's encryption key. Requires the property to be encrypted.
    /// </summary>
    /// <remarks>
    ///     The only strategy that reaches data no <c>UPDATE</c> can: backups keep the ciphertext, and
    ///     without the key nobody can read it.
    /// </remarks>
    DestroyKey = 5
}
