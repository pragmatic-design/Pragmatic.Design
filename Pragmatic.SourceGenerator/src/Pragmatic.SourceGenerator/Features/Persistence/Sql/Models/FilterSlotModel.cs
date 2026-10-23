namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A filter slot a plan's statements read: its family, the entity, the filter class, its name and its bypass; an empty part is absent.</summary>
internal sealed record FilterSlotModel(string Family, string Entity, string FilterClass, string Name, string Bypass);
