using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A plan's loader: it reads the companion statements' result sets in order and hangs the children on the parents' rows.</summary>
internal sealed record LoaderModel(string ParentsType, EquatableArray<LoadStepModel> Steps);
