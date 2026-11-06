namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>Where a <c>[GeneratedValue]</c> <c>{SEQ}</c> number is read from: the session of the engine that persists the entity (#562).</summary>
internal enum GeneratedValueEngine
{
    /// <summary>EF Core's <c>SequenceValueProvider</c>, over the boundary's <c>DbContext</c>.</summary>
    EfCore,

    /// <summary>A database sequence, through the boundary's <c>SqlSession</c>.</summary>
    Sql,

    /// <summary>A counter document, through the boundary's <c>DocSession</c>.</summary>
    Documents,
}
