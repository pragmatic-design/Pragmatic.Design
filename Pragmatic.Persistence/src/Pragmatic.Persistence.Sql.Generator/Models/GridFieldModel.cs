using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A field of a grid adapter: its name on the wire, the property, how it compares and its type, what the declaration allows.
///     <see cref="AdmittedOperators" /> is a <c>[GridField(AllowedOperators)]</c> restriction with its aliases; empty when there is none.
/// </summary>
internal sealed record GridFieldModel(
    string Json,
    string Property,
    GridFieldKind Kind,
    string Type,
    bool Nullable,
    bool Filterable,
    bool Sortable,
    EquatableArray<string> AdmittedOperators);
