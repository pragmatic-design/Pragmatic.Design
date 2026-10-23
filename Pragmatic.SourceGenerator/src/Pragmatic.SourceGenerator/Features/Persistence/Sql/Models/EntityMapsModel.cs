using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     <c>EntityMaps</c>: the intern pools, the maps and projections this assembly registers at module start with its functions, the readers
///     the plans call (for every entity this assembly queries) and the private setters the rows are built through.
/// </summary>
internal sealed record EntityMapsModel(
    EquatableArray<InternPoolModel> Pools,
    EquatableArray<EntityMapModel> Maps,
    EquatableArray<ProjectionMapModel> Projections,
    EquatableArray<FunctionRegistrationModel> Functions,
    EquatableArray<EntityReaderModel> Readers,
    EquatableArray<PrivateSetterModel> Setters);
