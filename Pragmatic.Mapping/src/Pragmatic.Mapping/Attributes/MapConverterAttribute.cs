using Pragmatic.Mapping.Converters;

namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Specifies a custom converter for a property — or, when applied at the CLASS level, for every
///     convention-mapped property whose source→target types match the converter's
///     <see cref="IValueConverter{TSource,TTarget}" /> signature (property-level wins; multiple
///     class-level converters may target different type pairs).
///     <para>
///         The converter must implement <see cref="IValueConverter{TSource,TTarget}" />
///         and have a parameterless constructor (stateless).
///     </para>
///     <para>
///         <strong>Note:</strong> Converters are not supported in projections (PRAG0320 warning).
///     </para>
/// </summary>
/// <typeparam name="TConverter">
///     The converter type. Must implement <see cref="IValueConverter{TSource, TTarget}" />
///     and have a parameterless constructor.
/// </typeparam>
/// <example>
///     <code>
///     public class MoneyToStringConverter : IValueConverter&lt;Money, string&gt;
///     {
///         public string Convert(Money source) => source.Format();
///         public Money ConvertBack(string target) => Money.Parse(target);
///     }
/// 
///     [MapFrom&lt;Order&gt;]
///     public partial record OrderDto
///     {
///         [MapConverter&lt;MoneyToStringConverter&gt;]
///         public string TotalFormatted { get; init; }
///     }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = true, Inherited = false)]
public sealed class MapConverterAttribute<TConverter> : Attribute
    where TConverter : class, new()
{
}