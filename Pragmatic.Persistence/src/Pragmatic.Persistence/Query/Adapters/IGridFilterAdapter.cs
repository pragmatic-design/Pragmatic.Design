namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Adapter interface: maps any external grid format to Pragmatic's canonical
///     <see cref="GridFilterRequest" />.
/// </summary>
/// <typeparam name="TExternalFormat">The external grid framework's request type.</typeparam>
/// <remarks>
///     <para>Implementations are pure mapping — no SG needed, no DI state.</para>
///     <para>
///         Example adapters: DevExpress DataSourceLoadOptions, AG Grid IServerSideGetRowsRequest,
///         PrimeNG LazyLoadEvent.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public class DevExpressGridAdapter : IGridFilterAdapter&lt;DataSourceLoadOptions&gt;
///     {
///         public GridFilterRequest Adapt(DataSourceLoadOptions input)
///         {
///             return new GridFilterRequest
///             {
///                 Filters = MapFilters(input.Filter),
///                 Sorts = MapSorts(input.Sort),
///                 Page = input.Skip / input.Take + 1,
///                 PageSize = input.Take
///             };
///         }
///     }
///     </code>
/// </example>
public interface IGridFilterAdapter<in TExternalFormat>
{
    /// <summary>
    ///     Converts an external grid request to the canonical format.
    /// </summary>
    /// <param name="input">The external grid framework's request.</param>
    /// <returns>A canonical <see cref="GridFilterRequest" />.</returns>
    GridFilterRequest Adapt(TExternalFormat input);
}
