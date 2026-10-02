namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a class as a source-generated grid adapter for the specified entity type.
///     The generator will create optimized, compile-time mapping from dynamic grid JSON
///     (DevExpress, PrimeNG, etc.) to strongly-typed LINQ expressions.
/// </summary>
/// <typeparam name="TEntity">The entity type to generate the adapter for.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GridAdapterAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>
    ///     The grid framework to generate adapter for.
    ///     Default is Both (DevExpress and PrimeNG).
    /// </summary>
    public GridFramework Framework { get; set; } = GridFramework.Both;

    /// <summary>
    ///     Whether to generate nested filter support (AND/OR trees).
    ///     Default is true.
    /// </summary>
    public bool SupportNestedFilters { get; set; } = true;

    /// <summary>
    ///     Whether to generate grouping support.
    ///     Default is true.
    /// </summary>
    public bool SupportGrouping { get; set; } = true;
}

/// <summary>
///     Specifies which grid framework(s) to generate adapters for.
/// </summary>
[Flags]
public enum GridFramework
{
    /// <summary>DevExpress DataGrid.</summary>
    DevExpress = 1,

    /// <summary>PrimeNG DataTable.</summary>
    PrimeNG = 2,

    /// <summary>Both DevExpress and PrimeNG.</summary>
    Both = DevExpress | PrimeNG
}
