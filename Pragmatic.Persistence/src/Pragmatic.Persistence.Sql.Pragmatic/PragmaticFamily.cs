namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>The families of a Pragmatic entity's filters, as the engine's slots name them (the same order as <c>SqlFilterFamily</c>).</summary>
internal enum PragmaticFamily : byte
{
    SoftDelete,
    Tenant,
    Permission,
    Rule,
    Temporal,
    ComputedScope,
}
