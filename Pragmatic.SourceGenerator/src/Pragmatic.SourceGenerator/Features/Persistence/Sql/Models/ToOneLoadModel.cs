using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A to-one include whose objects carry collections: the objects the parents hold, the parents of the steps under it.</summary>
internal sealed record ToOneLoadModel(string Name, string Parents, string Property, EquatableArray<LoadStepModel> Children, string TargetType)
    : LoadStepModel(Name, Parents, Property, Children);
