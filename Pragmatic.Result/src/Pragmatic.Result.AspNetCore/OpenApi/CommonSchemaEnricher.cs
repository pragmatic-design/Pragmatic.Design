using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pragmatic.Result.AspNetCore.OpenApi;

/// <summary>
/// Schema transformer that adds proper format annotations for common CLR types.
/// </summary>
/// <remarks>
/// Ensures OpenAPI consumers see accurate format hints:
/// <list type="bullet">
///     <item><see cref="Guid"/>: <c>format: uuid</c></item>
///     <item><see cref="DateTime"/> / <see cref="DateTimeOffset"/>: <c>format: date-time</c></item>
///     <item><see cref="DateOnly"/>: <c>format: date</c></item>
///     <item><see cref="TimeOnly"/>: <c>format: time</c></item>
///     <item><see cref="Uri"/>: <c>format: uri</c></item>
///     <item><see cref="TimeSpan"/>: <c>format: duration</c></item>
/// </list>
/// </remarks>
public sealed class CommonSchemaEnricher : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        var format = GetFormat(type);
        // Only fill in Format when it isn't already set: an explicitly-configured Format
        // (from another enricher or an attribute) must be preserved, so we avoid
        // last-writer-wins clobbering of a caller-provided value.
        if (format is not null && string.IsNullOrEmpty(schema.Format))
            schema.Format = format;

        return Task.CompletedTask;
    }

    private static string? GetFormat(Type type) => type switch
    {
        _ when type == typeof(Guid) => "uuid",
        _ when type == typeof(DateTime) => "date-time",
        _ when type == typeof(DateTimeOffset) => "date-time",
        _ when type == typeof(DateOnly) => "date",
        _ when type == typeof(TimeOnly) => "time",
        _ when type == typeof(Uri) => "uri",
        _ when type == typeof(TimeSpan) => "duration",
        _ => null
    };
}
