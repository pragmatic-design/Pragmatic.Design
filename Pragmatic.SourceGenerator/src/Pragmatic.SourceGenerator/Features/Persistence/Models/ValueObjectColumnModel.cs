namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One column produced by flattening a <c>[ValueObject]</c> entity property into an EF Core complex
///     type. Named <c>{Property}_{SubProperty}</c> (EF's default complex-type convention). Carries the
///     inputs <c>SqlTypeMapper</c> needs so the migration schema column matches the EF model column.
/// </summary>
internal sealed record ValueObjectColumnModel
{
    /// <summary>Column name: <c>{Property}_{SubProperty}</c>.</summary>
    public required string ColumnName { get; init; }

    /// <summary>Fully-qualified CLR type of the value object's sub-property.</summary>
    public required string TypeName { get; init; }

    /// <summary>Whether the column is nullable.</summary>
    public bool IsNullable { get; init; }

    /// <summary>Whether the sub-property is an enum (mapped to its integer type).</summary>
    public bool IsEnum { get; init; }

    /// <summary>Optional string max length.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Optional decimal precision.</summary>
    public int? Precision { get; init; }

    /// <summary>Optional decimal scale.</summary>
    public int? Scale { get; init; }
}
