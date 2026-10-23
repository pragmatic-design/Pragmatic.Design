namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>Why an entity's persistence engine cannot be decided.</summary>
internal enum PersistenceEngineProblemKind
{
    /// <summary>Two or more engines are referenced and the entity's boundary names none (<c>PRAG0743</c>).</summary>
    NoEngineNamed,

    /// <summary>The boundary names an engine the compilation does not reference (<c>PRAG0744</c>).</summary>
    EngineNotReferenced
}
