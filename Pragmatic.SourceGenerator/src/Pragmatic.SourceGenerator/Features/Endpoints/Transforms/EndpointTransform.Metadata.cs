using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Metadata parsing methods (API versions, processors, OpenAPI, groups).
/// </summary>
internal static partial class EndpointTransform
{
    private static ImmutableArray<ApiVersionModel> ParseApiVersions(INamedTypeSymbol symbol)
    {
        var versions = symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.ApiVersion)
            .Select(attr =>
            {
                var version = attr.ConstructorArguments.Length > 0
                    ? attr.ConstructorArguments[0].Value?.ToString() ?? "1.0"
                    : "1.0";

                var deprecated = false;
                string? deprecationMessage = null;
                string? sunsetDate = null;

                foreach (var namedArg in attr.NamedArguments)
                    switch (namedArg.Key)
                    {
                        case "Deprecated":
                            deprecated = (bool)(namedArg.Value.Value ?? false);
                            break;
                        case "DeprecationMessage":
                            deprecationMessage = namedArg.Value.Value?.ToString();
                            break;
                        case "SunsetDate":
                            sunsetDate = namedArg.Value.Value?.ToString();
                            break;
                    }

                return new ApiVersionModel
                {
                    Version = version,
                    Deprecated = deprecated,
                    DeprecationMessage = deprecationMessage,
                    SunsetDate = sunsetDate
                };
            })
            .ToImmutableArray();

        return versions;
    }

    private static ImmutableArray<ProcessorModel> ParseProcessors(INamedTypeSymbol symbol, bool isPreProcessor)
    {
        var attrName = isPreProcessor ? EndpointAttributeNames.PreProcessor : EndpointAttributeNames.PostProcessor;

        // ⚠️ Matched on the original definition's *metadata* name. ToDisplayString() renders a generic
        // definition as PreProcessorAttribute<TProcessor>, never as PreProcessorAttribute`1, so a
        // comparison of the display string against the backtick constant is false for every
        // [PreProcessor<T>]: the endpoint compiles and generates no pipeline. The processor's interface
        // needs no check here — the attribute's `where TProcessor : IEndpoint{Pre,Post}Processor`
        // constraint makes the compiler refuse any other type.
        var processors = symbol.GetAttributes()
            .Where(a =>
            {
                var definition = a.AttributeClass?.OriginalDefinition;
                if (definition is null) return false;
                return $"{definition.ContainingNamespace.ToDisplayString()}.{definition.MetadataName}" == attrName;
            })
            .Select(attr =>
            {
                string? typeName = null;
                INamedTypeSymbol? processorSymbol = null;
                var order = 0;

                if (attr.AttributeClass?.TypeArguments.Length > 0)
                {
                    processorSymbol = attr.AttributeClass.TypeArguments[0] as INamedTypeSymbol;
                    typeName = attr.AttributeClass.TypeArguments[0]
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }

                foreach (var namedArg in attr.NamedArguments)
                    if (namedArg.Key == "Order")
                        order = (int)(namedArg.Value.Value ?? 0);

                return typeName is not null
                    ? new ProcessorModel
                    {
                        TypeName = typeName,
                        Order = order,
                        NotConstructibleReason = DescribeWhyNotConstructible(processorSymbol)
                    }
                    : null;
            })
            .Where(p => p is not null)
            .OrderBy(p => p!.Order)
            .Select(p => p!)
            .ToImmutableArray();

        return processors;
    }

    /// <summary>
    ///     Why <c>IServiceCollection</c> cannot hold a working registration for this processor, or
    ///     <c>null</c> when it can.
    /// </summary>
    /// <remarks>
    ///     The container activates through public constructors only, so an abstract type — an interface
    ///     included, which the generic form's constraint also accepts — and a type whose constructors
    ///     are all non-public have no registration that resolves. Reported as PRAG0534 rather than
    ///     registered, because a registration for such a type fails when the endpoint is called and
    ///     not when the application starts.
    /// </remarks>
    private static string? DescribeWhyNotConstructible(INamedTypeSymbol? processor)
    {
        if (processor is null)
            return null;

        if (processor.TypeKind == TypeKind.Interface)
            return "it is an interface";

        if (processor.IsAbstract)
            return "it is abstract";

        if (processor.IsStatic)
            return "it is static";

        return processor.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public)
            ? null
            : "it has no public constructor";
    }

    private static (string? Summary, string? Description, ImmutableArray<string> Tags)
        ParseOpenApiMetadata(INamedTypeSymbol symbol)
    {
        string? summary = null;
        string? description = null;
        var tags = ImmutableArray<string>.Empty;

        foreach (var attr in symbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();

            if (attrName == EndpointAttributeNames.ApiSummary && attr.ConstructorArguments.Length > 0)
                summary = attr.ConstructorArguments[0].Value?.ToString();
            else if (attrName == EndpointAttributeNames.ApiDescription && attr.ConstructorArguments.Length > 0)
                description = attr.ConstructorArguments[0].Value?.ToString();
            else if (attrName == EndpointAttributeNames.ApiTags)
                tags = attr.ConstructorArguments
                    .SelectMany(a => a.Kind == TypedConstantKind.Array
                        ? a.Values.Select(v => v.Value?.ToString() ?? string.Empty)
                        : new[] { a.Value?.ToString() ?? string.Empty })
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToImmutableArray();
        }

        // The attribute wins where it is written, and the doc comment answers everywhere else — which
        // is nearly everywhere, since [ApiSummary] is a thing an author has to think to add and
        // the <summary> above the class is a thing they already wrote.
        summary ??= GetXmlDocSummary(symbol);

        return (summary, description, tags);
    }

    private static int? ParseSuccessStatusCode(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.HttpStatus);

        if (attr is not null && attr.ConstructorArguments.Length > 0)
            return (int)(attr.ConstructorArguments[0].Value ?? 200);

        return null;
    }

    private static (ImmutableArray<ExampleModel> Request, ImmutableArray<ResponseExampleModel> Response)
        ParseExamples(INamedTypeSymbol symbol)
    {
        var requestExamples = ImmutableArray.CreateBuilder<ExampleModel>();
        var responseExamples = ImmutableArray.CreateBuilder<ResponseExampleModel>();

        foreach (var attr in symbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();

            if (attrName == EndpointAttributeNames.RequestExample && attr.ConstructorArguments.Length > 0)
            {
                var json = attr.ConstructorArguments[0].Value?.ToString() ?? "";
                string? name = null;
                string? summary = null;
                foreach (var namedArg in attr.NamedArguments)
                    switch (namedArg.Key)
                    {
                        case "Name":
                            name = namedArg.Value.Value?.ToString();
                            break;
                        case "Summary":
                            summary = namedArg.Value.Value?.ToString();
                            break;
                    }

                requestExamples.Add(new ExampleModel
                {
                    Json = json,
                    Name = name,
                    Summary = summary,
                    IsValidJson = Pragmatic.SourceGen.JsonValidator.IsValid(json)
                });
            }
            else if (attrName == EndpointAttributeNames.ResponseExample && attr.ConstructorArguments.Length > 1)
            {
                var statusCode = attr.ConstructorArguments[0].Value is int code ? code : 200;
                var json = attr.ConstructorArguments[1].Value?.ToString() ?? "";
                string? name = null;
                foreach (var namedArg in attr.NamedArguments)
                    if (namedArg.Key == "Name")
                        name = namedArg.Value.Value?.ToString();

                responseExamples.Add(new ResponseExampleModel
                {
                    StatusCode = statusCode,
                    Json = json,
                    Name = name,
                    IsValidJson = Pragmatic.SourceGen.JsonValidator.IsValid(json)
                });
            }
        }

        return (requestExamples.ToImmutable(), responseExamples.ToImmutable());
    }

    private static McpToolModel? ParseMcpTool(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.McpTool);

        if (attr is null)
            return null;

        string? name = null;
        string? description = null;
        foreach (var namedArg in attr.NamedArguments)
            switch (namedArg.Key)
            {
                case "Name":
                    name = namedArg.Value.Value?.ToString();
                    break;
                case "Description":
                    description = namedArg.Value.Value?.ToString();
                    break;
            }

        return new McpToolModel { Name = name, Description = description };
    }

    private static int? ParseSseHeartbeat(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == "Pragmatic.Endpoints.Attributes.SseAttribute");

        if (attr is null)
            return null;

        foreach (var namedArg in attr.NamedArguments)
            if (namedArg.Key == "HeartbeatSeconds")
                return (int)(namedArg.Value.Value ?? 0);

        return null;
    }

    private static IdempotencyModel? ParseIdempotency(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.Idempotent);

        if (attr is null)
            return null;

        var durationSeconds = 0;
        string? headerName = null;
        foreach (var namedArg in attr.NamedArguments)
            switch (namedArg.Key)
            {
                case "DurationSeconds":
                    durationSeconds = (int)(namedArg.Value.Value ?? 0);
                    break;
                case "HeaderName":
                    headerName = namedArg.Value.Value?.ToString();
                    break;
            }

        return new IdempotencyModel { DurationSeconds = durationSeconds, HeaderName = headerName };
    }

    private static long? ParseMaxBodySize(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.MaxBodySize);

        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is long bytes)
            return bytes;

        return null;
    }

    private static string? ParseCreatedAtTemplate(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.CreatedAt);

        return attr is { ConstructorArguments.Length: > 0 }
            ? attr.ConstructorArguments[0].Value?.ToString()
            : null;
    }
}
