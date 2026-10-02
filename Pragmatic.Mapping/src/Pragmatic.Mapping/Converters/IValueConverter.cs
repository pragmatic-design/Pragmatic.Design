namespace Pragmatic.Mapping.Converters;

/// <summary>
///     Defines a bidirectional value converter for custom type mappings.
///     <para>
///         Implementations must be stateless and have a parameterless constructor.
///         The generator instantiates converters using <c>new TConverter()</c>.
///     </para>
///     <para>
///         <strong>Note:</strong> Converters are not supported in projections
///         as they cannot be translated to SQL.
///     </para>
/// </summary>
/// <typeparam name="TSource">The source value type.</typeparam>
/// <typeparam name="TTarget">The target value type.</typeparam>
/// <example>
///     <code>
///     public class MoneyToStringConverter : IValueConverter&lt;Money, string&gt;
///     {
///         public string Convert(Money source) => $"{source.Currency} {source.Amount:F2}";
///         public Money ConvertBack(string target) => Money.Parse(target);
///     }
/// 
///     public class EnumToIntConverter&lt;TEnum&gt; : IValueConverter&lt;TEnum, int&gt;
///         where TEnum : struct, Enum
///     {
///         public int Convert(TEnum source) => System.Convert.ToInt32(source);
///         public TEnum ConvertBack(int target) => (TEnum)Enum.ToObject(typeof(TEnum), target);
///     }
///     </code>
/// </example>
public interface IValueConverter<TSource, TTarget>
{
    /// <summary>
    ///     Converts a value from source type to target type.
    ///     <para>
    ///         Used in <c>[MapFrom]</c> scenarios when mapping entity → DTO.
    ///     </para>
    /// </summary>
    /// <param name="source">The source value to convert.</param>
    /// <returns>The converted target value.</returns>
    TTarget Convert(TSource source);

    /// <summary>
    ///     Converts a value from target type back to source type.
    ///     <para>
    ///         Used in <c>[MapTo]</c> scenarios when mapping DTO → entity.
    ///     </para>
    /// </summary>
    /// <param name="target">The target value to convert back.</param>
    /// <returns>The converted source value.</returns>
    TSource ConvertBack(TTarget target);
}