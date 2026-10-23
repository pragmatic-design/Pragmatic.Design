namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A <c>[RollUp]</c> rule kept by the child's writes: the parent's table, key and column, the child's table and foreign key, the summed column (null for a count), the child's live column.</summary>
internal sealed record RollUpModel(string ParentTable, string ParentKeyColumn, string Column, string ChildTable, string ForeignKey, string? AmountColumn, string? LiveColumn);
