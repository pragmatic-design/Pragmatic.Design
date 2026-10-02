using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
///     A scaffolded operation lands in the group its entity's hand-written operations are in.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It landed on the boundary <b>root</b>, and by the rules as written: a group is inferred
///         from the operation's namespace, an entity's namespace is flat whatever its folder
///         (<c>{Module}.Entities</c>), and <c>Entities</c> is one of the segments the inference
///         discards. <c>[Resource]</c> gives its synthesised operations the entity's namespace, so
///         there was no segment to read.
///     </para>
///     <para>
///         The consequence is a seam in the facade nobody chose: <c>catalog.Amenities.CreateAmenity(…)</c>
///         beside <c>catalog.ResourceListAmenity(…)</c>, one grouped and one not, only because one was
///         generated. The group is not inferred here — it is <b>carried</b>: the operations of that
///         entity already say which group it belongs to, and <c>SubBoundaryName</c> is the field the
///         boundary already prefers over the namespace.
///     </para>
/// </remarks>
public class AResourceOperationJoinsItsEntitysGroupTests
{
    private const string Source = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;

        namespace App.Catalog
        {
            [Boundary]
            public partial class CatalogBoundary;

            [Module]
            public partial class CatalogModule;

            [PragmaticDbContext("Catalog")]
            public partial class CatalogDbContext { }
        }

        namespace App.Catalog.Entities
        {
            // The entity is filed under Amenities/, and its namespace is flat all the same.
            [Entity]
            [Resource("amenities", Capabilities = ResourceCapabilities.Read | ResourceCapabilities.List)]
            public partial class Amenity : IEntity
            {
                public string Name { get; set; } = "";
            }
        }

        namespace App.Catalog.Amenities.Mutations
        {
            using App.Catalog.Entities;

            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;

            // The hand-written neighbour: this is what puts the entity in the Amenities group.
            [Mutation(Mode = MutationMode.Create)]
            [Endpoint(HttpVerb.Post, "api/amenities")]
            public partial class CreateAmenityMutation : Mutation<Amenity>
            {
                public string Name { get; init; } = "";
            }
        }
        """;

    /// <summary>The generated read and list are members of the group, not of the root.</summary>
    [Fact]
    public void TheScaffoldedOperations_AreMembersOfTheEntitysGroup()
    {
        var group = Generated("ICatalogAmenitiesActions");

        group.Should().NotBeNull("the hand-written mutation makes the group exist");
        group!.Should().Contain("ResourceReadAmenity",
            "a generated operation is reached by the same name as its neighbours");
        group.Should().Contain("ResourceListAmenity");
    }

    /// <summary>
    ///     And the root carries the group, not the operations.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The assertion with teeth, and the one the consumer's suite makes: a root that carries
    ///     both properties and operation methods is a module where some operations were grouped and
    ///     others were not.
    /// </remarks>
    [Fact]
    public void TheRoot_CarriesTheGroupAndNotTheOperations()
    {
        var root = Generated("ICatalogActions");

        root.Should().NotBeNull();
        root!.Should().Contain("Amenities", "the group is a property of the root");
        root.Should().NotContain("ResourceReadAmenity",
            "an operation on the root is one that was left out of the groups");
        root.Should().NotContain("ResourceListAmenity");
    }

    /// <summary>
    ///     The control: an entity with no hand-written operation anywhere keeps its place on the root.
    /// </summary>
    /// <remarks>
    ///     Without it, "the operations are grouped" could be satisfied by inventing a group per entity
    ///     — which would name a group after a folder nobody created, and put a lone entity's scaffolding
    ///     behind a facade property that exists for it alone.
    /// </remarks>
    [Fact]
    public void AnEntityWithNoOperationsOfItsOwn_KeepsItsScaffoldingOnTheRoot()
    {
        // The same module, with the hand-written mutation writing a different entity: nothing puts
        // Amenity in a group.
        var source = Source.Replace("Mutation<Amenity>", "Mutation<Other>")
            .Replace("public string Name { get; init; } = \"\";", "public string Note { get; init; } = \"\";")
            .Replace("""
                namespace App.Catalog.Amenities.Mutations
                """, """
                namespace App.Catalog.Others.Mutations
                """)
            .Replace("""
                    public partial class Amenity : IEntity
                    {
                        public string Name { get; set; } = "";
                    }
                """, """
                    public partial class Amenity : IEntity
                    {
                        public string Name { get; set; } = "";
                    }

                    [Entity]
                    public partial class Other : IEntity
                    {
                        public string Note { get; set; } = "";
                    }
                """);

        var root = Generated("ICatalogActions", source);

        root.Should().NotBeNull();
        root!.Should().Contain("ResourceReadAmenity",
            "no operation of its own means no group to join");
    }

    /// <summary>
    ///     The body of one generated interface, and nothing else.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not the file: the root and its groups are generated into the <b>same</b> file, so a test
    ///     that reads the file cannot tell "the operation is in the group" from "the operation is on
    ///     the root". The first version of this case passed for exactly that reason.
    /// </remarks>
    private static string? Generated(string interfaceName, string? source = null)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source ?? Source);

        var file = sources.Values.FirstOrDefault(v => v.Contains($"interface {interfaceName}"));

        if (file is null)
            return null;

        var start = file.IndexOf($"interface {interfaceName}", System.StringComparison.Ordinal);
        var open = file.IndexOf('{', start);
        if (open < 0)
            return null;

        var depth = 0;
        for (var i = open; i < file.Length; i++)
        {
            if (file[i] == '{') depth++;
            else if (file[i] == '}' && --depth == 0)
                return file.Substring(start, i - start + 1);
        }

        return null;
    }
}
