using System.ComponentModel;
using System.Globalization;

namespace Pragmatic.Internationalization.Types;

/// <summary>
///     TypeConverter for <see cref="CultureCode"/> so that IConfiguration.Bind() can
///     deserialize "en-US" strings from appsettings.json into CultureCode? properties.
/// </summary>
internal sealed class CultureCodeTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
            return (CultureCode)s; // uses CultureCode implicit operator

        return base.ConvertFrom(context, culture, value);
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    /// <inheritdoc />
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is CultureCode code)
            return (string)code; // uses CultureCode implicit operator

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
