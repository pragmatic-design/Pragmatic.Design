namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A <c>[DatabaseGenerated]</c> column read back after a write: its column, its property, and its reader at its place in the read (over <c>r</c> and <c>d</c>).</summary>
internal sealed record GeneratedColumnModel(string Column, string Property, string Read);
