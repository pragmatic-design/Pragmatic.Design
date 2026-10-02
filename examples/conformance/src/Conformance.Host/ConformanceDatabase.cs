using Pragmatic.Composition.Attributes;
using Pragmatic.Composition.Database;
using Pragmatic.Composition.Enums;

namespace Conformance.Host;

/// <summary>
///     The single database, hosting every boundary.
/// </summary>
/// <remarks>
///     Only one until the matrix has a cell that requires two databases — the write that crosses a
///     transactional boundary. Until then a second one would add surface without demonstrating anything.
/// </remarks>
[PragmaticDatabase(Provider = DatabaseProvider.PostgreSql, ConfigKey = "ConnectionStrings:Conformance")]
public sealed class ConformanceDatabase : PragmaticDatabase;
