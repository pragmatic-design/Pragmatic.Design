namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>How a plan reads one row.</summary>
internal enum PlanReaderKind
{
    /// <summary>A scalar: the plan reads the value itself.</summary>
    None,

    /// <summary>The entity's reader in <c>EntityMaps</c>, as a method group.</summary>
    Method,

    /// <summary>A lambda building the row (a projection, an aggregate's value, the rows a command reads back).</summary>
    Lambda,

    /// <summary>The entity's reader, then its to-one includes read from the columns after its own.</summary>
    Include,
}
