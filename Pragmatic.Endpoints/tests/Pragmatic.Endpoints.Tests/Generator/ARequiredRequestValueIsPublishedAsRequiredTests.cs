using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A query, header or form value carrying Pragmatic.Validation's <c>[Required]</c> is published as
///     required, in the manifest and in the description the runtime document reads.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It was published as optional. Requiredness was read from the C# <c>required</c> keyword
///         only, so <c>[Required] public string Name</c> reached both documents as
///         <c>"isRequired": false</c>, while validation refused a request without it with 422. A client
///         generated from the contract made it optional and learned otherwise at run time.
///     </para>
///     <para>
///         The binding does not change: the value is still bound as optional and validation does the
///         refusing, so the answer stays 422, not a 400 from the binder.
///     </para>
/// </remarks>
public class ARequiredRequestValueIsPublishedAsRequiredTests : EndpointsGeneratorTestBase
{
    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;
        using Pragmatic.Validation.Attributes;

        namespace TestApp.Orders;

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/orders")]
        public partial class PlaceOrderAction : DomainAction<string>
        {
            [FromQuery]
            [Required]
            public string Name { get; set; } = null!;

            [FromQuery]
            public decimal Amount { get; set; }

            [FromHeader(Name = "X-Trace")]
            [Required]
            public string? Trace { get; set; }

            // A nullable value type: the one form field the binder treats as optional (a nullable
            // reference type is bound as required — a separate defect).
            [FromForm]
            [Required]
            public int? Copies { get; set; }

            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success(Name));
        }
        """;

    private const string Description = "global::Pragmatic.Endpoints.ApiExplorer.PragmaticParameterDescription";
    private const string ParameterSource = "global::Pragmatic.Endpoints.ApiExplorer.PragmaticParameterSource";

    /// <summary>The parameters the manifest publishes for the one endpoint, by name.</summary>
    private static Dictionary<string, JsonElement> ManifestParameters()
    {
        var manifest = GetGeneratedSource(RunGenerator(Source), "_Metadata.PragmaticManifest")!;

        // The compact copy the assembly attribute carries, which is what a host reads back.
        const string open = "\"1.0.0\", \"\"\"";
        var start = manifest.IndexOf(open, StringComparison.Ordinal) + open.Length;
        var end = manifest.IndexOf("\"\"\")]", start, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(manifest[start..end]);

        return document.RootElement.GetProperty("endpoints")[0].GetProperty("parameters")
            .EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.Clone());
    }

    [Fact]
    public void TheManifest_PublishesAQueryValueCarryingRequired_AsRequired()
    {
        ManifestParameters()["name"].GetProperty("isRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TheManifest_PublishesAHeaderCarryingRequired_AsRequired()
    {
        ManifestParameters()["X-Trace"].GetProperty("isRequired").GetBoolean().Should().BeTrue();
    }

    /// <summary>The control: a value with no <c>[Required]</c> stays optional.</summary>
    [Fact]
    public void TheManifest_LeavesAValueWithoutRequired_Optional()
    {
        ManifestParameters()["amount"].GetProperty("isRequired").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void TheRuntimeDescription_SaysRequired_AndTheControlDoesNot()
    {
        var handler = GetGeneratedSource(RunGenerator(Source), "PlaceOrderAction.Endpoint")!;

        handler.Should().Contain($"new {Description}(\"name\", {ParameterSource}.Query, typeof(string), true)");
        handler.Should().Contain($"new {Description}(\"X-Trace\", {ParameterSource}.Header, typeof(string), true)");
        handler.Should().Contain($"new {Description}(\"Copies\", {ParameterSource}.Form, typeof(int), true)",
            "the binding treats an int? form field as optional, and validation still requires it");
        handler.Should().Contain($"new {Description}(\"amount\", {ParameterSource}.Query, typeof(decimal), false)");
    }

    /// <summary>
    ///     The binding is unchanged: the value is not refused by the binder, which would answer 400 before
    ///     validation could answer 422.
    /// </summary>
    [Fact]
    public void TheBinding_StillTreatsTheValueAsOptional()
    {
        var handler = GetGeneratedSource(RunGenerator(Source), "PlaceOrderAction.Endpoint")!;

        handler.Should().NotContain("WriteAsync(httpContext, \"name\", \"the value is missing or malformed\")");
    }
}
