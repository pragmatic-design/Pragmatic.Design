using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Endpoints.Diagnostics;
using Pragmatic.Serialization;

namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     A JSON response written by the generated writer of its type, straight into the response body, when the
///     host's options are the ones the writer reproduces; by the serializer otherwise.
/// </summary>
/// <typeparam name="T">The response type the writer was generated for.</typeparam>
/// <remarks>
///     <para>
///         The status, the <c>Location</c> and the content type are the ones <c>Results.Ok</c>,
///         <c>Results.Created</c> and <c>Results.Json</c> set, and the fallback is that same call, so the response
///         is the one the endpoint answered with before, written another way.
///     </para>
///     <para>
///         <b>The value's own type decides, as it did.</b> The generated handlers used to pass the value as an
///         <c>object</c>, and the serializer writes an object as its runtime type. The writer was planned for
///         <typeparamref name="T" />, so a value of a type derived from it goes to the serializer. A sealed type, a
///         struct and an interface (the list a query answers with, written element by element through the
///         declared element type either way) cannot differ.
///     </para>
///     <para>
///         ⚠️ The document is written whole into the body's buffer and flushed once. The serializer flushes as it
///         goes past a threshold; for a very large response this holds more of it in memory at once.
///     </para>
/// </remarks>
public sealed class GeneratedJsonResponse<T> : IResult, IStatusCodeHttpResult
{
    private const string ContentType = "application/json; charset=utf-8";

    [ThreadStatic] private static Utf8JsonWriter? t_writer;

    private readonly T _value;
    private readonly string? _location;
    private readonly Action<Utf8JsonWriter, T> _write;
    private readonly GeneratedJsonShape _shape;

    /// <param name="value">The response.</param>
    /// <param name="statusCode">The status it answers with.</param>
    /// <param name="location">The <c>Location</c> of a <c>201</c>, or null.</param>
    /// <param name="write">The generated writer of <typeparamref name="T" />.</param>
    /// <param name="shape">What the writer writes, emitted beside it.</param>
    public GeneratedJsonResponse(T value, int statusCode, string? location, Action<Utf8JsonWriter, T> write, GeneratedJsonShape shape)
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(write);
        Pragmatic.Ensure.Ensure.ThrowIfNull(shape);
        _value = value;
        StatusCode = statusCode;
        _location = location;
        _write = write;
        _shape = shape;
    }

    /// <inheritdoc />
    public int StatusCode { get; }

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        Pragmatic.Ensure.Ensure.ThrowIfNull(httpContext);

        if (!WritesItself(httpContext))
        {
            EndpointResponseMetrics.JsonResponses.Add(1, new KeyValuePair<string, object?>(EndpointResponseMetrics.WriterTag, "serializer"));
            return Fallback(httpContext).ExecuteAsync(httpContext);
        }

        EndpointResponseMetrics.JsonResponses.Add(1, new KeyValuePair<string, object?>(EndpointResponseMetrics.WriterTag, "generated"));

        var response = httpContext.Response;
        if (!string.IsNullOrEmpty(_location))
            response.Headers.Location = _location;

        response.StatusCode = StatusCode;
        response.ContentType = ContentType;

        Write(response.BodyWriter);
        return response.BodyWriter.FlushAsync(httpContext.RequestAborted).AsTask();
    }

    private bool WritesItself(HttpContext httpContext)
    {
        if (_value is null)
            return false;

        if (!typeof(T).IsSealed && !typeof(T).IsValueType && !typeof(T).IsInterface && _value.GetType() != typeof(T))
            return false;

        var options = httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions;
        return options is not null && GeneratedJsonDefaults.AllowGeneratedWriters(options, _shape);
    }

    /// <summary>Writes the document synchronously, on one thread, with the writer that thread keeps.</summary>
    private void Write(IBufferWriter<byte> body)
    {
        // Validation is the serializer's to skip as well: the writer is generated from the type, so the shape is right
        // by construction. The encoder is the one the options the writer is used under carry.
        var writer = t_writer ??= new Utf8JsonWriter(body, new JsonWriterOptions
        {
            Encoder = GeneratedJsonDefaults.ResponseEncoder,
            SkipValidation = true,
        });
        writer.Reset(body);
        try
        {
            _write(writer, _value);
            writer.Flush();
        }
        finally
        {
            // Not holding the response's buffer past this call: the next request on this thread brings its own.
            writer.Reset(Stream.Null);
        }
    }

    /// <summary>The call the handler made before it had a writer.</summary>
    /// <remarks>
    ///     Any other status goes through the overload that takes the type's metadata from the host's options rather
    ///     than the one that takes the options: the second is annotated as reflection-bound, and resolves the same
    ///     metadata from the same options anyway.
    /// </remarks>
    private IResult Fallback(HttpContext httpContext) => StatusCode switch
    {
        StatusCodes.Status200OK => Results.Ok(_value),
        StatusCodes.Status201Created => Results.Created(_location, _value),
        _ => Results.Json(_value, (JsonTypeInfo<T>)HostOptions(httpContext).GetTypeInfo(typeof(T)), statusCode: StatusCode),
    };

    private static JsonSerializerOptions HostOptions(HttpContext httpContext)
        => (httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value ?? new JsonOptions()).SerializerOptions;
}
