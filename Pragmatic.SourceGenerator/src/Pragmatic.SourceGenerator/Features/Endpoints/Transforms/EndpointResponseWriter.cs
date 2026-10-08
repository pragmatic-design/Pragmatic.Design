using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Serialization.Analysis;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Gives an endpoint the generated writer of what it answers with, or the reason it keeps the serializer.
/// </summary>
/// <remarks>
///     <para>
///         The type is read back from <see cref="EndpointModel.ResponseType" />, the text every handler template
///         answers with, so the writer is planned for the type the handler actually writes, an endpoint the
///         generator assembled itself (a resource's CRUD, a trait's) included.
///     </para>
///     <para>
///         A type that does not resolve is one this generator writes in the same compilation — the record a
///         key-returning mutation answers with — and cannot be planned from a symbol it does not have yet. It keeps
///         the serializer, and nothing is reported: there is nothing the author could change.
///     </para>
/// </remarks>
internal static class EndpointResponseWriter
{
    public static EndpointModel With(EndpointModel model, Compilation compilation, JsonTypeExpressionResolver? resolver = null)
    {
        if (!AnswersWithJson(model))
            return model;

        var type = (resolver ?? new JsonTypeExpressionResolver(compilation)).Resolve(model.ResponseType!);
        if (type is null)
            return model;

        var plan = JsonResponseWriterPlanner.TryPlan(type, compilation, out var refusal);
        if (plan is null)
            return model with { ResponseWriterRefusal = refusal };

        var writers = Redaction.Templates.RedactionMapTemplate.NamespaceFor(compilation.AssemblyName ?? "");
        return model with
        {
            ResponseWriter = plan,
            ResponseWriterDelegate = Serialization.Templates.Utf8JsonWritersTemplate.ResponseDelegateFor(writers, plan.EntryMethod),
            ResponseWriterShape = Serialization.Templates.Utf8JsonWritersTemplate.ResponseShapeFor(writers, plan.EntryMethod),
        };
    }

    /// <summary>
    ///     Whether the handler answers with a JSON body written in one place: not nothing, not a file, not a
    ///     stream of events, not one of several versions each with a body of its own.
    /// </summary>
    private static bool AnswersWithJson(EndpointModel model)
        => model is { IsValid: true, IsVoid: false, IsStreamingResponse: false, HasActionVersioning: false, ResponseType.Length: > 0 }
           && !model.IsFileResponse
           && model.ComputedSuccessStatusCode != 204;
}
