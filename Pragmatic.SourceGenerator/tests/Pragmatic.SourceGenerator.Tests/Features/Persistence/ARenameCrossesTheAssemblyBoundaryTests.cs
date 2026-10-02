using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[RenamedFrom]</c> reaches the host, which is the only place a migration is written.
/// </summary>
/// <remarks>
///     <para>
///         A module publishes its entities as a JSON payload and the host reads them back: that payload
///         is the only way the host knows an entity of another assembly. It carried the name, the type,
///         the nullability, the precision and the defaults — and not the rename. So the attribute was
///         read by the module's generator, dropped on the way out, and the host's diff saw a column
///         added and a column removed: a rename became a <b>drop with the data in it</b>.
///     </para>
///     <para>
///         ⚠️ And it worked in the one place nobody keeps entities — the host's own assembly — which is
///         why nothing had noticed. The effect is measured end to end in
///         <c>Conformance.Tests.Cases.TheRenamedColumn</c>, against a database built at the previous
///         version; this is the half that can be measured in a second.
///     </para>
/// </remarks>
public class ARenameCrossesTheAssemblyBoundaryTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Composition.Attributes.ModuleAttribute>()
    ];

    [Fact]
    public void ARenamedColumn_IsPublishedToWhoeverWritesTheMigration()
        => Payload("[RenamedFrom(\"Price\")]")
            .Should().Contain("\"renamedFrom\": \"Price\"");

    /// <summary>
    ///     The control: a column nobody renamed publishes nothing.
    /// </summary>
    /// <remarks>
    ///     Otherwise "the rename is published" would be satisfied by a payload that carries the key for
    ///     every property — which would make every column look renamed from itself, and the diff would
    ///     have to decide what that means.
    /// </remarks>
    [Fact]
    public void AColumnNobodyRenamed_PublishesNothing()
        => Payload(declaration: "").Should().NotContain("renamedFrom");

    /// <summary>The module's persistence metadata for an item whose price column is declared as given.</summary>
    private static string Payload(string declaration)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Catalog
            {
                [Boundary]
                public partial class CatalogBoundary;
            }

            namespace Contoso.Catalog.Entities
            {
                [Entity]
                public partial class CatalogItem : IEntity
                {
                    public string Name { get; private set; } = "";

                    {{declaration}}
                    public decimal ListPrice { get; private set; }
                }
            }
            """, References);

        var files = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        files.Should().ContainKey("_Metadata.Persistence.g.cs");

        return files["_Metadata.Persistence.g.cs"];
    }
}
