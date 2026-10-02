using Pragmatic.Mapping.Converters;
using Pragmatic.Persistence.EFCore.Samples.Entities;

namespace Pragmatic.Persistence.EFCore.Samples.Converters;

/// <summary>
///     Converts OrderStatus enum to/from string representation.
///     Useful for APIs that send status as string.
/// </summary>
public class OrderStatusToStringConverter : IValueConverter<OrderStatus, string>
{
    public string Convert(OrderStatus source) => source.ToString();

    public OrderStatus ConvertBack(string target)
    {
        return Enum.TryParse<OrderStatus>(target, ignoreCase: true, out var result)
            ? result
            : OrderStatus.Pending;
    }
}

/// <summary>
///     Converts CustomerType enum to/from integer.
///     Useful for systems that use numeric codes.
/// </summary>
public class CustomerTypeToIntConverter : IValueConverter<CustomerType, int>
{
    public int Convert(CustomerType source) => (int)source;

    public CustomerType ConvertBack(int target)
    {
        return Enum.IsDefined(typeof(CustomerType), target)
            ? (CustomerType)target
            : CustomerType.Individual;
    }
}

/// <summary>
///     Converts boolean to "Y"/"N" string format.
///     Common in legacy systems.
/// </summary>
public class BoolToYesNoConverter : IValueConverter<bool, string>
{
    public string Convert(bool source) => source ? "Y" : "N";

    public bool ConvertBack(string target)
        => target.Equals("Y", StringComparison.OrdinalIgnoreCase)
           || target.Equals("Yes", StringComparison.OrdinalIgnoreCase)
           || target.Equals("1", StringComparison.Ordinal);
}
