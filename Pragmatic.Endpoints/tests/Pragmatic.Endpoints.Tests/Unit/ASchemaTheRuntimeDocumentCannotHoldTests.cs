using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Endpoints.ApiExplorer;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     A type whose schema ASP.NET's OpenAPI document cannot read back is described without it, and
///     every other type keeps its schema.
/// </summary>
/// <remarks>
///     <para>
///         The document reads each schema back through a writer limited to 64 levels, and a type past
///         that fails the whole document with a 500. Found on the Showcase, where mutations answer with
///         their entity: measured once resolved, <c>Reservation</c> nests 79 levels and <c>Guest</c> 103.
///     </para>
///     <para>
///         The types here are built to a known depth. A <see cref="Box{T}" /> adds two levels — its object
///         and its <c>properties</c> — so <c>Box</c> nested <i>n</i> times over a number nests 2n + 1.
///     </para>
/// </remarks>
public class ASchemaTheRuntimeDocumentCannotHoldTests
{
    /// <summary>The control: a type well inside the limit keeps its schema.</summary>
    [Fact]
    public void AResponseInsideTheLimit_KeepsItsSchema()
    {
        var response = DescribeResponse(Boxed(20)).SupportedResponseTypes.Single();

        response.Type.Should().Be(Boxed(20), "41 levels fit in 64");
    }

    /// <summary>
    ///     A type the JSON schema exporter itself refuses is described without its schema.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Forty boxes never reach the document's writer: the exporter has its own depth limit and
    ///     throws first. The document calls the same exporter, so it would fail the same way — and the
    ///     guard, measuring with it, takes the refusal as an answer rather than throwing out of the API
    ///     explorer.
    /// </remarks>
    [Fact]
    public void AResponseTheExporterRefuses_IsDescribedWithoutItsSchema_ButStillAsJson()
    {
        var response = DescribeResponse(Boxed(40)).SupportedResponseTypes.Single();

        response.Type.Should().BeNull("the exporter refuses 81 levels, and the document would fail on them");
        response.ApiResponseFormats.Select(f => f.MediaType).Should().Contain("application/json",
            "the operation still answers JSON; only the shape is left to the compile-time document");
    }

    /// <summary>
    ///     ⚠️ A recursion is counted at the depth the document resolves it to, not at the exporter's.
    /// </summary>
    /// <remarks>
    ///     The exporter cuts the loop back to <see cref="Deep" /> with a reference near the bottom, and
    ///     accepts the type: seventeen boxes are well inside its limit. The document then replaces that
    ///     reference with a copy of <see cref="Deep" />'s whole schema, which roughly doubles the depth
    ///     and takes it past 64. The first version of the guard measured the exporter's output, let the
    ///     Showcase's entities through, and the document still failed. This is the case that proves the
    ///     depth branch; the forty boxes prove the exporter's refusal.
    /// </remarks>
    [Fact]
    public void ARecursionThatResolvesPastTheLimit_IsDescribedWithoutItsSchema()
    {
        DescribeResponse(typeof(Deep)).SupportedResponseTypes.Single().Type.Should().BeNull();
    }

    /// <summary>The control: a recursive type is not cut for being recursive.</summary>
    [Fact]
    public void AShallowRecursion_KeepsItsSchema()
    {
        DescribeResponse(typeof(Tree)).SupportedResponseTypes.Single().Type.Should().Be(typeof(Tree));
    }

    /// <summary>A body the document cannot hold is still a body: described as any JSON, not dropped.</summary>
    [Fact]
    public void ABodyTheDocumentCannotHold_IsDescribedAsAnyJson()
    {
        var description = Describe(new PragmaticRequestDescription(
            new PragmaticParameterDescription("body", PragmaticParameterSource.Body, Boxed(40), true)));

        var body = description.ParameterDescriptions.Single();
        body.Source.Should().Be(BindingSource.Body);
        body.Type.Should().Be(typeof(JsonElement));
    }

    private static ApiDescription DescribeResponse(Type type)
        => Describe(new PragmaticRequestDescription(), new ProducesResponseTypeMetadata(200, type));

    private static ApiDescription Describe(params object[] metadata)
    {
        var endpoint = new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/things"),
            order: 0,
            new EndpointMetadataCollection([new HttpMethodMetadata(["POST"]), .. metadata]),
            "POST /things");

        var provider = new PragmaticApiDescriptionProvider(
            new DefaultEndpointDataSource(endpoint),
            new HostingEnvironment { ApplicationName = "Tests" },
            Options.Create(new JsonOptions()),
            NullLogger<PragmaticApiDescriptionProvider>.Instance);

        var context = new ApiDescriptionProviderContext([]);
        provider.OnProvidersExecuting(context);

        return context.Results.Single();
    }

    /// <summary><see cref="Box{T}" /> nested <paramref name="levels" /> times over <c>int</c>.</summary>
    private static Type Boxed(int levels)
    {
        var type = typeof(int);
        for (var i = 0; i < levels; i++)
            type = typeof(Box<>).MakeGenericType(type);

        return type;
    }

    public class Box<T>
    {
        public T Value { get; set; } = default!;
    }

    /// <summary>Seventeen boxes deep, with a way back to the top at the bottom.</summary>
    public sealed class Deep : Box<Box<Box<Box<Box<Box<Box<Box<Box<Box<Box<Box<Box<Box<Box<Box<Loop>>>>>>>>>>>>>>>>;

    public sealed class Loop
    {
        public Deep? Back { get; set; }
    }

    public sealed class Tree
    {
        public Tree? Left { get; set; }

        public Tree? Right { get; set; }
    }
}
