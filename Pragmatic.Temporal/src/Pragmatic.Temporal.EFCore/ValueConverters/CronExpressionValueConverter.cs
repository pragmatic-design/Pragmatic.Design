using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for <see cref="CronExpression" /> to string.
/// </summary>
public sealed class CronExpressionValueConverter : ValueConverter<CronExpression, string>
{
    /// <summary>Creates a new instance.</summary>
    public CronExpressionValueConverter()
        : base(
            c => c.Expression,
            s => CronExpression.Parse(s))
    {
    }
}

