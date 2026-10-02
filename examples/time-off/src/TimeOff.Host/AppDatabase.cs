using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace TimeOff.Host;

/// <summary>
///     The one database: a leave request, the allowance it draws on and the audit of its decision are
///     written in the same transaction.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:App")]
public sealed class AppDatabase : PragmaticDatabase;
