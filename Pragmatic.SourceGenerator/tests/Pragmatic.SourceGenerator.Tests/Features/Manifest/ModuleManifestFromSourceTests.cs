using System.Linq;
using System.Text.Json;
using Pragmatic.Actions.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     What a module's manifest says about a request body, starting from the C# that declares it.
/// </summary>
/// <remarks>
///     <para>
///         The seam nothing tested. The manifest writer is covered by tests that construct a model, and
///         the OpenAPI generator by tests that feed a hand-written manifest — so a field the reader
///         reads and the writer never writes passes both and is missing on the only path that runs.
///         That is exactly what happened to <c>maxLength</c>: the OpenAPI generator has always read it
///         for request bodies, the model has always had it, and nothing in between ever put it there.
///     </para>
///     <para>
///         These tests start at the declaration and read the manifest the module actually emits, so a
///         break anywhere along transform → model → writer shows up here.
///     </para>
/// </remarks>
public class ModuleManifestFromSourceTests
{
    private static JsonElement RequestBodyProperties(string declaration, string operationSuffix)
    {
        var manifest = Manifest(declaration);

        foreach (var endpoint in manifest.GetProperty("endpoints").EnumerateArray())
        {
            if (endpoint.GetProperty("operationId").GetString()?.Contains(operationSuffix) == true)
                return endpoint.GetProperty("requestBody").GetProperty("properties");
        }

        var ids = string.Join(", ", manifest.GetProperty("endpoints").EnumerateArray()
            .Select(e => e.GetProperty("operationId").GetString()));
        throw new Xunit.Sdk.XunitException(
            $"No endpoint whose operationId contains '{operationSuffix}'. Present: [{ids}]");
    }

    private static JsonElement Manifest(string declaration)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Mapping.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;
            using Pragmatic.Validation.Attributes;

            namespace Catalogue;

            public sealed class ThingBoundary;

            public enum Shelf { Cold, Ambient }

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Thing : IEntity
            {
                public string Name { get; private set; } = "";
            }

            {{declaration}}
            """,
            GeneratorTestHelper.FromType<CompositeActionAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Validation.Attributes.MaxLengthAttribute>(),
            // Without this the attribute does not resolve, ReadJsonPropertyName sees no AttributeClass,
            // and the wire name is silently absent — which is how three earlier attempts at this test
            // failed while the feature worked in a real application.
            GeneratorTestHelper.FromType<global::System.Text.Json.Serialization.JsonPropertyNameAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Authorization.RequirePermissionAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "_Metadata.PragmaticManifest");

        // Not asserted as "the corpus compiles": a generator harness references only what each test
        // needs, so the output compilation always carries errors about types it was never given. What
        // matters is that the manifest came out — and when it does not, the errors say why, which
        // beats an assertion failing on a null with no clue in it.
        if (string.IsNullOrEmpty(generated))
        {
            var errors = string.Join(
                " | ",
                GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString()).Take(8));
            throw new Xunit.Sdk.XunitException($"No manifest was generated. Compilation errors: {errors}");
        }

        return JsonDocument.Parse(ManifestTestHarness.ConstantValue(generated!, "Json")).RootElement;
    }

    private static JsonElement TypeProperties(string declaration, string simpleName)
    {
        var manifest = Manifest(declaration);

        foreach (var type in manifest.GetProperty("types").EnumerateArray())
        {
            if (type.GetProperty("type").GetString()?.EndsWith(simpleName) == true)
                return type.GetProperty("properties");
        }

        var names = string.Join(", ", manifest.GetProperty("types").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()));
        throw new Xunit.Sdk.XunitException($"No manifest type named '{simpleName}'. Present: [{names}]");
    }

    private static JsonElement Named(JsonElement properties, string name)
    {
        foreach (var p in properties.EnumerateArray())
            if (p.GetProperty("name").GetString() == name)
                return p;

        throw new Xunit.Sdk.XunitException($"No manifest property called '{name}'.");
    }

    /// <summary>
    ///     A declared maximum length reaches the manifest, which is the only way it can reach the
    ///     published contract.
    /// </summary>
    [Fact]
    public void ADeclaredMaxLength_ReachesTheManifest()
    {
        var properties = RequestBodyProperties("""
            [Mutation(Mode = MutationMode.Create)]
            [Endpoint(HttpVerb.Post, "api/things")]
            public partial class CreateThingMutation : Mutation<Thing>
            {
                [MaxLength(120)]
                public required string Name { get; init; }
            }
            """, "CreateThing");

        Named(properties, "Name").GetProperty("maxLength").GetInt32().Should().Be(120,
            "the OpenAPI generator has always read this field, and nothing ever wrote it");
    }

    /// <remarks>
    ///     The other half of the same silence: a constraint that is absent must stay absent rather than
    ///     appear as a zero, which a client generator would enforce as "the empty string only".
    /// </remarks>
    [Fact]
    public void NoDeclaredMaxLength_WritesNoField()
    {
        var properties = RequestBodyProperties("""
            [Mutation(Mode = MutationMode.Create)]
            [Endpoint(HttpVerb.Post, "api/things")]
            public partial class CreateThingMutation : Mutation<Thing>
            {
                public required string Name { get; init; }
            }
            """, "CreateThing");

        Named(properties, "Name").TryGetProperty("maxLength", out _).Should().BeFalse();
    }

    /// <summary>
    ///     Every constraint the server applies and JSON Schema can spell reaches the manifest —
    ///     not only <c>maxLength</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generated validator refuses a string under <c>[MinLength]</c>, a number outside
    ///         <c>[Range]</c>, a text that fails <c>[Regex]</c>; the manifest carried none of them, so
    ///         the published contract described a server more permissive than the one answering, and a
    ///         client generated from it learned each rule at its first 422.
    ///     </para>
    ///     <para>
    ///         ⚠️ A length rule on a collection counts elements, not characters, and the document has
    ///         to say <c>maxItems</c> for it: <c>maxLength</c> on an array is a keyword no reader
    ///         applies. That was the one constraint that did arrive, and on a collection it arrived
    ///         under the wrong name.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheConstraintsTheServerApplies_ReachTheManifest()
    {
        var properties = RequestBodyProperties("""
            [Mutation(Mode = MutationMode.Create)]
            [Endpoint(HttpVerb.Post, "api/things")]
            public partial class CreateThingMutation : Mutation<Thing>
            {
                [MinLength(3)] [MaxLength(60)] public required string Name { get; init; }
                [Length(2, 4)] public required string Code { get; init; }
                [Regex("^[A-Z]{3}$")] public required string Prefix { get; init; }
                [Email] public required string Contact { get; init; }
                [Url] public required string Site { get; init; }
                [Range(1, 10)] public int Shelf { get; init; }
                [GreaterThan(0)] public decimal Price { get; init; }
                [LessThanOrEqual(100)] public int Percent { get; init; }
                [MinCount(1)] [MaxCount(5)] public required List<string> Tags { get; init; }
                [MaxLength(2)] public required List<string> Aliases { get; init; }
            }
            """, "CreateThing");

        var name = Named(properties, "Name");
        name.GetProperty("minLength").GetInt32().Should().Be(3);
        name.GetProperty("maxLength").GetInt32().Should().Be(60);

        var code = Named(properties, "Code");
        code.GetProperty("minLength").GetInt32().Should().Be(2);
        code.GetProperty("maxLength").GetInt32().Should().Be(4);

        Named(properties, "Prefix").GetProperty("pattern").GetString().Should().Be("^[A-Z]{3}$");
        Named(properties, "Contact").GetProperty("format").GetString().Should().Be("email");
        Named(properties, "Site").GetProperty("format").GetString().Should().Be("uri");

        var shelf = Named(properties, "Shelf");
        shelf.GetProperty("minimum").GetDouble().Should().Be(1);
        shelf.GetProperty("maximum").GetDouble().Should().Be(10);

        Named(properties, "Price").GetProperty("exclusiveMinimum").GetDouble().Should().Be(0);
        Named(properties, "Percent").GetProperty("maximum").GetDouble().Should().Be(100);

        var tags = Named(properties, "Tags");
        tags.GetProperty("minItems").GetInt32().Should().Be(1);
        tags.GetProperty("maxItems").GetInt32().Should().Be(5);

        var aliases = Named(properties, "Aliases");
        aliases.GetProperty("maxItems").GetInt32().Should().Be(2,
            "[MaxLength] on a collection counts elements");
        aliases.TryGetProperty("maxLength", out _).Should().BeFalse(
            "maxLength on an array is a keyword no reader applies");
    }

    /// <summary>
    ///     A response DTO's constraints reach the manifest too — the same reader, asked of a type the
    ///     endpoint answers with rather than one it takes.
    /// </summary>
    [Fact]
    public void AResponseDtoConstraint_ReachesTheManifest()
    {
        var properties = TypeProperties("""
            [MapFrom<Thing>]
            public partial class ThingDto
            {
                [MaxLength(40)] public string Name { get; init; } = "";
                [Range(0, 5)] public int Stars { get; init; }
            }

            [Query<Thing, ThingDto>]
            [Endpoint(HttpVerb.Get, "api/things")]
            public partial class ListThingsQuery { }
            """, "ThingDto");

        Named(properties, "Name").GetProperty("maxLength").GetInt32().Should().Be(40);
        Named(properties, "Stars").GetProperty("maximum").GetDouble().Should().Be(5);
    }

    /// <summary>
    ///     The wire name travels from the declaration to the manifest the document is built from.
    /// </summary>
    /// <remarks>
    ///     The request DTO honoured <c>[JsonPropertyName]</c> from the start, so the endpoint accepted
    ///     the renamed field while the published schema kept advertising the property's own name. This
    ///     is the step in between, where the two stopped agreeing.
    /// </remarks>
    [Fact]
    public void AWireName_ReachesTheManifest()
    {
        var properties = RequestBodyProperties("""
            [Mutation(Mode = MutationMode.Create)]
            [Endpoint(HttpVerb.Post, "api/things")]
            public partial class CreateThingMutation : Mutation<Thing>
            {
                [JsonPropertyName("label")]
                public required string Name { get; init; }

                public string Note { get; init; } = "";
            }
            """, "CreateThing");

        Named(properties, "Name").GetProperty("wireName").GetString().Should().Be("label");
        Named(properties, "Note").TryGetProperty("wireName", out _).Should().BeFalse(
            "a name that is not overridden is written once, by the reader, not twice by both");
    }

    /// <remarks>
    ///     The manifest carries the declared CLR type, which is what lets the document say "array of
    ///     string" instead of publishing every non-primitive as a string.
    /// </remarks>
    [Fact]
    public void ACollectionAndAnEnum_KeepTheirDeclaredShape()
    {
        var properties = RequestBodyProperties("""
            [Mutation(Mode = MutationMode.Create)]
            [Endpoint(HttpVerb.Post, "api/things")]
            public partial class CreateThingMutation : Mutation<Thing>
            {
                public required string Name { get; init; }
                public List<string>? Keywords { get; init; }
                public Shelf Shelf { get; init; }
            }
            """, "CreateThing");

        Named(properties, "Keywords").GetProperty("type").GetString()
            .Should().Contain("List", "the item type is what the document turns into an items schema");
        Named(properties, "Shelf").GetProperty("isEnum").GetBoolean().Should().BeTrue(
            "an enum outside the manifest is a string on the wire, and only this flag says so");
    }

    /// <summary>
    ///     A response DTO that renames a field on the wire is described under that name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The other half of the request-body case, and it stayed open one round longer because
    ///         nothing in the repository renamed a response field — so there was nothing to measure and
    ///         the gap was stated rather than seen. It is the same defect: the serializer honours
    ///         <c>[JsonPropertyName]</c>, the schema published the property's own name, and a client
    ///         generated from the document reads a field the response does not have. Silently, since a
    ///         missing member deserialises to a default rather than to an error.
    ///     </para>
    ///     <para>
    ///         Worse than the request half, in fact: a request under the wrong name is refused, and a
    ///         response under the wrong name is a null nobody reports.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AResponseDtoWireName_ReachesTheManifest()
    {
        var properties = TypeProperties("""
            [MapFrom<Thing>]
            public partial class ThingDto
            {
                [JsonPropertyName("label")]
                public string Name { get; init; } = "";

                public int Weight { get; init; }
            }

            [Query<Thing, ThingDto>]
            [Endpoint(HttpVerb.Get, "api/things")]
            public partial class SearchThingsQuery
            {
                public string? Name { get; init; }
            }
            """, "ThingDto");

        Named(properties, "Name").GetProperty("wireName").GetString().Should().Be("label",
            "the document is what a client is generated from, and it must name what the response carries");
        Named(properties, "Weight").TryGetProperty("wireName", out _).Should().BeFalse();
    }
}
