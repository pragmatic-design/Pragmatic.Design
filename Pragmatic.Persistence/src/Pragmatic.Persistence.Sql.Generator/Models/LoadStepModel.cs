using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A step of a plan's loader, under the parents held by <see cref="Parents" />: <see cref="Name" /> prefixes its locals, the steps under it
///     (<see cref="Children" />) follow it.
/// </summary>
internal abstract record LoadStepModel(string Name, string Parents, string Property, EquatableArray<LoadStepModel> Children);
