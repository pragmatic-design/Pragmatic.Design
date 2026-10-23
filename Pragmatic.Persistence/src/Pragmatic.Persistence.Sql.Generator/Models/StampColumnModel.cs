namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A column the writer stamps: its property and column, and, for a clock, whether it holds a <c>DateTime</c> (else a <c>DateTimeOffset</c>).</summary>
internal sealed record StampColumnModel(string Property, string Column, bool IsDateTime);
