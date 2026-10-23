namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>The reader the plans call for an entity: <c>Read_{Name}</c>, building the row with <see cref="Construct" /> over <c>r</c> and <c>d</c>.</summary>
internal sealed record EntityReaderModel(string EntityType, string Name, string Construct);
