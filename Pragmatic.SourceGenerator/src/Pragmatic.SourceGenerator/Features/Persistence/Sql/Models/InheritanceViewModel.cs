using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     The view a table-per-concrete-type root, or a table-per-type one (<see cref="Tpt" />), is read through: its name, the tables it reads
///     (the root's own, <see cref="WriteTable" />, for a table per type), and its body, as written and for SQL Server.
/// </summary>
internal sealed record InheritanceViewModel(
    string? Namespace,
    string RootName,
    string View,
    bool Tpt,
    string? WriteTable,
    EquatableArray<string> Tables,
    string Select,
    string SelectSqlServer) : TrailerSectionModel(Namespace);
