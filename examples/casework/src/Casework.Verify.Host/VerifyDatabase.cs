using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace Casework.Verify.Host;

/// <summary>
///     Verify's own database. Intake has another, and a verification's row is never in the same
///     transaction as the case that asked for it — which is exactly what the outbox exists to
///     make safe.
/// </summary>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Verify")]
public sealed class VerifyDatabase : PragmaticDatabase;
