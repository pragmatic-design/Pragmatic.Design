using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.ValueConverters;

/// <summary>
///     EF Core value converter for nullable <see cref="CronExpression" /> to nullable string.
/// </summary>
public sealed class NullableCronExpressionValueConverter : ValueConverter<CronExpression?, string?>
{
    /// <summary>Creates a new instance.</summary>
    public NullableCronExpressionValueConverter()
        : base(
            c => c != null ? c.Expression : null,
            s => !string.IsNullOrEmpty(s) ? CronExpression.Parse(s) : null)
    {
    }
}
