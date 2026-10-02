using System;
using System.Linq;
using System.Text.Json;
using Pragmatic.Actions.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     The manifest's catalogues say the same thing whatever order the operations were declared in.
/// </summary>
/// <remarks>
///     <para>
///         The manifest is a published document that other tools read and diff. A catalogue whose order
///         follows discovery order changes when a file moves between folders, and a reader then cannot
///         tell a real change from a rename: a pure relocation of five files leaves every route, verb,
///         operationId and per-endpoint permission unchanged as a set, and returns <c>endpoints</c>,
///         <c>types</c> and <c>permissions</c> in a different order in all three of an application's
///         manifests.
///     </para>
///     <para>
///         ⚠️ Preserving discovery order looks like the stable choice, because sorting rewrites the
///         manifest of every assembly. That rewrite happens once; discovery order rewrites the manifest
///         every time somebody moves a file. The neighbouring <c>AsyncApiTemplate</c> sorts its channels
///         for the same reason, a name being what consumers bind to.
///     </para>
/// </remarks>
public class TheManifestCataloguesAreOrderedTests
{
    private static string[] PermissionNames(string operations)
        => [.. Manifest(operations).GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()!)];

    private static string[] OperationIds(string operations)
        => [.. Manifest(operations).GetProperty("endpoints").EnumerateArray()
            .Select(e => e.GetProperty("operationId").GetString()!)];

    private static JsonElement Manifest(string operations)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Authorization;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Catalogue;

            public sealed class ThingBoundary;

            [Entity]
            [BelongsTo<ThingBoundary>]
            public partial class Thing : IEntity
            {
                public string Name { get; private set; } = "";
            }

            {{operations}}
            """,
            GeneratorTestHelper.FromType<CompositeActionAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Entity.EntityAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Authorization.RequirePermissionAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "_Metadata.PragmaticManifest");
        if (string.IsNullOrEmpty(generated))
        {
            var errors = string.Join(
                " | ",
                GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString()).Take(8));
            throw new Xunit.Sdk.XunitException($"No manifest was generated. Compilation errors: {errors}");
        }

        return JsonDocument.Parse(ManifestTestHarness.ConstantValue(generated!, "Json")).RootElement;
    }

    private const string Reads = """
        [DomainAction]
        [RequirePermission("catalogue.thing.read")]
        [Endpoint(HttpVerb.Get, "api/things/read")]
        public partial class ReadThingAction : VoidDomainAction;
        """;

    private const string Archives = """
        [DomainAction]
        [RequirePermission("catalogue.thing.archive")]
        [Endpoint(HttpVerb.Post, "api/things/archive")]
        public partial class ArchiveThingAction : VoidDomainAction;
        """;

    private const string Writes = """
        [DomainAction]
        [RequirePermission("catalogue.thing.write")]
        [Endpoint(HttpVerb.Post, "api/things/write")]
        public partial class WriteThingAction : VoidDomainAction;
        """;

    [Fact]
    public void TheSameOperationsInADifferentOrder_ProduceTheSamePermissionArray()
    {
        var oneWay = PermissionNames($"{Reads}\n{Archives}\n{Writes}");
        var theOther = PermissionNames($"{Writes}\n{Reads}\n{Archives}");

        oneWay.Should().NotBeEmpty("the three operations each declare a permission");
        oneWay.Should().Equal(theOther,
            "the array is part of a published document, so its order belongs to the document rather "
            + "than to the order the compiler happened to hand the operations over");
    }

    /// <summary>
    ///     The same holds for the endpoint catalogue, which churned the same way and by more entries.
    /// </summary>
    /// <remarks>
    ///     Measured on the relocation that started this: the endpoint list came back in a different
    ///     order in all three of an application's manifests, with the same operations in it. That one
    ///     also reaches the published OpenAPI document, whose paths are built from this list.
    /// </remarks>
    [Fact]
    public void TheEndpointCatalogue_IsOrderedTheSameWay()
    {
        var oneWay = OperationIds($"{Reads}\n{Archives}\n{Writes}");
        var theOther = OperationIds($"{Writes}\n{Reads}\n{Archives}");

        oneWay.Should().NotBeEmpty("the three operations each publish a route");
        oneWay.Should().Equal(theOther, "the catalogue is part of the same published document");
        oneWay.Should().Equal(
            [.. oneWay.OrderBy(id => id, StringComparer.Ordinal)],
            "by operationId, which is the name a reader of this document binds to");
    }

    /// <summary>The order is the names', ascending — a rule a reader can predict, not merely stable.</summary>
    /// <remarks>
    ///     "Same both ways" alone would also be satisfied by any deterministic scramble. Naming the
    ///     rule is what lets someone reading a diff know where an added permission will appear.
    /// </remarks>
    [Fact]
    public void TheOrderIsAlphabetical()
    {
        var names = PermissionNames($"{Reads}\n{Archives}\n{Writes}");

        names.Should().Equal(
            ["catalogue.thing.archive", "catalogue.thing.read", "catalogue.thing.write"],
            "ordinal by name, which is what the document promises");
    }
}
