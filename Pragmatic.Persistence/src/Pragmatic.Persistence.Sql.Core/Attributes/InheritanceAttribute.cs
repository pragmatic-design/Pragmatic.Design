namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On the root of a hierarchy stored in one table (the repository's <c>[Inheritance(Tph)]</c>): the derived <c>[Entity]</c> classes
///     share the root's table, their own columns nullable, and a text column names each row's type. <c>Query&lt;Root&gt;()</c> reads
///     every row as its own type; <c>Query&lt;Derived&gt;()</c> reads the rows of that type; the writers set the discriminator.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InheritanceAttribute : Attribute
{
    public InheritanceAttribute()
    {
    }

    /// <summary>The repository's form: <c>[Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "FeeType")]</c>.</summary>
    public InheritanceAttribute(InheritanceStrategy strategy) => Strategy = strategy;

    /// <summary>The discriminator column; <c>Discriminator</c> by default. It holds the type's name.</summary>
    public string Discriminator { get; init; } = "Discriminator";

    /// <summary>The repository's name for <see cref="Discriminator" />; it wins when both are set.</summary>
    public string? DiscriminatorColumn { get; init; }

    /// <summary>
    ///     How the hierarchy is stored: one table (<see cref="InheritanceStrategy.Tph" />, the default), or one table per concrete type
    ///     (<see cref="InheritanceStrategy.Tpc" />: each derived type its own table with the inherited columns; the root's <c>[Table]</c>
    ///     names a view over them, <c>UNION ALL</c> with the type's name as the discriminator, whose DDL the generator emits as
    ///     <c>{Root}View.CreateSql(dialect)</c>; a write through the root goes to the row's concrete table).
    /// </summary>
    public InheritanceStrategy Strategy { get; init; } = InheritanceStrategy.Tph;
}
