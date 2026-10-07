using Pragmatic.Privacy;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>
///     A logged type a generated writer does not describe: a dictionary member. It declares personal data, so it
///     is masked the classic way, by the declared redactor.
/// </summary>
public sealed record Ledger(
    string Account,
    [property: PersonalData(DataCategory.Financial)] string Iban,
    Dictionary<string, string> Entries);
