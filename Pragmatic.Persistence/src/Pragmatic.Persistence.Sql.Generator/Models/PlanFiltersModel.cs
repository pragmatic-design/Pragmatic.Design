using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>The filter families of Pragmatic's entities in a plan: the slots in their numbering, and the first parameter their values take.</summary>
internal sealed record PlanFiltersModel(EquatableArray<FilterSlotModel> Slots, int FirstValue);
