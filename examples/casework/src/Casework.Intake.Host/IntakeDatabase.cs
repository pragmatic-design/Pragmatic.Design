using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace Casework.Intake.Host;

/// <summary>
///     Intake's own database. Verify has another, and neither service reads the other's.
/// </summary>
/// <remarks>
///     Sharing one database between the two would make them two modules of a monolith — which is
///     Invoicing, and already exists. A tenant may have a database of its own within this
///     one's server; this type stays the service's default and the shared one.
/// </remarks>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Intake")]
public sealed class IntakeDatabase : PragmaticDatabase;
