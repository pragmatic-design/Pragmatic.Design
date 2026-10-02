using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     Integration tests for the RelationTransform and RelationGraphBuilder pipelines.
///     Runs the actual PragmaticSourceGenerator on source code with [Relation.*] attributes
///     and verifies the generated .Relations.g.cs output.
/// </summary>
public class RelationTransformTests
{
    [Fact]
    public void Convention_OneToMany_GeneratesCollectionAndChildFK()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToMany<Child>]
            public partial class Parent
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            public partial class Child
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        // Parent should get ICollection<Child> Childs (convention: simple pluralization)
        var parentRelations = FindRelationsSource(result, "Parent");
        parentRelations.Should().NotBeNull("Parent.Relations.g.cs should be generated");
        parentRelations.Should().Contain("ICollection<Child> Childs");

        // Child should get ParentId FK + Parent nav
        var childRelations = FindRelationsSource(result, "Child");
        childRelations.Should().NotBeNull("Child.Relations.g.cs should be generated");
        childRelations.Should().Contain("ParentId");
        childRelations.Should().Contain("Parent Parent");
    }

    [Fact]
    public void Convention_ManyToOne_GeneratesFKAndNav()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            public partial class Target
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.ManyToOne<Target>]
            public partial class Owner
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var ownerRelations = FindRelationsSource(result, "Owner");
        ownerRelations.Should().NotBeNull("Owner.Relations.g.cs should be generated");
        ownerRelations.Should().Contain("TargetId");
        ownerRelations.Should().Contain("Target Target");
    }

    [Fact]
    public void WithNavigation_ExplicitName()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToMany<OrderLine>.WithNavigation("Lines")]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            public partial class OrderLine
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var orderRelations = FindRelationsSource(result, "Order");
        orderRelations.Should().NotBeNull("Order.Relations.g.cs should be generated");
        orderRelations.Should().Contain("ICollection<OrderLine> Lines");
        // Should NOT contain the convention-derived name "OrderLines"
        orderRelations.Should().NotContain("OrderLines");
    }

    [Fact]
    public void WithNavigation_Inverse_SetsChildNavName()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToMany<Item>.WithNavigation("Items", Inverse = "ParentOrder")]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            public partial class Item
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        // Child should get the inverse nav name "ParentOrder" instead of default "Order"
        var itemRelations = FindRelationsSource(result, "Item");
        itemRelations.Should().NotBeNull("Item.Relations.g.cs should be generated");
        itemRelations.Should().Contain("ParentOrder");
        // FK should derive from inverse: "ParentOrderId"
        itemRelations.Should().Contain("ParentOrderId");
    }

    [Fact]
    public void CrossBoundary_ManyToOne_GeneratesFKOnly()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            public class BoundaryA {}
            public class BoundaryB {}

            [Entity]
            [BelongsTo<BoundaryA>]
            public partial class External
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [BelongsTo<BoundaryB>]
            [Relation.ManyToOne<External>]
            public partial class Local
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var localRelations = FindRelationsSource(result, "Local");
        localRelations.Should().NotBeNull("Local.Relations.g.cs should be generated");
        // Should have FK
        localRelations.Should().Contain("ExternalId");
        // Should NOT have navigation (cross-boundary)
        localRelations.Should().NotContain("External External");
    }

    [Fact]
    public void ManyToMany_GeneratesCollectionsOnBothSides()
    {
        // Both entities need [Relation.*] to trigger the inverse side generation
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.ManyToMany<Tag>]
            public partial class Post
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.ManyToMany<Post>]
            public partial class Tag
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var postRelations = FindRelationsSource(result, "Post");
        postRelations.Should().NotBeNull("Post.Relations.g.cs should be generated");
        postRelations.Should().Contain("ICollection<Tag> Tags");

        var tagRelations = FindRelationsSource(result, "Tag");
        tagRelations.Should().NotBeNull("Tag.Relations.g.cs should be generated");
        tagRelations.Should().Contain("ICollection<Post> Posts");
    }

    [Fact]
    public void OneToMany_ChildWithSourceFK_SkipsDuplicate()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToMany<LineItem>]
            public partial class Invoice
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            public partial class LineItem
            {
                public System.Guid PersistenceId { get; set; }
                public System.Guid InvoiceId { get; private set; }
            }
            """;

        var result = RunGenerator(source);

        var lineItemRelations = FindRelationsSource(result, "LineItem");
        lineItemRelations.Should().NotBeNull("LineItem.Relations.g.cs should be generated");
        // Should NOT contain duplicate InvoiceId (already in source)
        lineItemRelations.Should().NotContain("InvoiceId");
        // Should contain Invoice navigation (not in source)
        lineItemRelations.Should().Contain("Invoice Invoice");
    }

    [Fact]
    public void OneToMany_ChildWithSourceNav_SkipsDuplicate()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToMany<Detail>]
            public partial class Master
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            public partial class Detail
            {
                public System.Guid PersistenceId { get; set; }
                public System.Guid MasterId { get; private set; }
                public Master Master { get; set; } = null!;
            }
            """;

        var result = RunGenerator(source);

        var detailRelations = FindRelationsSource(result, "Detail");
        // Both MasterId and Master are already in source, so no Relations file needed for Detail
        // (or if generated, should NOT contain duplicates)
        if (detailRelations is not null)
        {
            detailRelations.Should().NotContain("Master Master",
                "Master nav already exists in source and should not be regenerated");
            detailRelations.Should().NotContain("MasterId",
                "MasterId FK already exists in source and should not be regenerated");
        }
    }

    [Fact]
    public void MultipleOneToMany_SameEntity_GeneratesAll()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToMany<LineItem>]
            [Relation.OneToMany<Payment>]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            public partial class LineItem
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            public partial class Payment
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var orderRelations = FindRelationsSource(result, "Order");
        orderRelations.Should().NotBeNull("Order.Relations.g.cs should be generated");
        orderRelations.Should().Contain("ICollection<LineItem> LineItems");
        orderRelations.Should().Contain("ICollection<Payment> Payments");
    }

    [Fact]
    public void NoRelationAttributes_NoRelationsFileGenerated()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            public partial class Simple
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var simpleRelations = FindRelationsSource(result, "Simple");
        simpleRelations.Should().BeNull("Simple has no [Relation.*] attributes, so no Relations file expected");
    }

    [Fact]
    public void ManyToMany_WithJoinTable_GeneratesConfig()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.ManyToMany<Tag>.WithNavigation("Tags", Inverse = "Posts", JoinTable = "PostTags")]
            public partial class Post
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.ManyToMany<Post>.WithNavigation("Posts", Inverse = "Tags")]
            public partial class Tag
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var postRelations = FindRelationsSource(result, "Post");
        postRelations.Should().NotBeNull();
        postRelations.Should().Contain("ICollection<Tag> Tags");

        // EntityConfiguration is now generated at host level only (DbContextFeature),
        // so module-level tests should NOT expect it.
        var postConfig = FindConfigurationSource(result, "Post");
        postConfig.Should().BeNull("EntityConfiguration is generated at host level, not module level");
    }

    [Fact]
    public void ManyToMany_WithJoinEntity_GeneratesConfig()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.ManyToMany<Tag, PostTag>.WithNavigation("Tags")]
            public partial class Post
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.ManyToMany<Post, PostTag>.WithNavigation("Posts")]
            public partial class Tag
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            public partial class PostTag
            {
                public System.Guid PersistenceId { get; set; }
                public bool IsPrimary { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var postRelations = FindRelationsSource(result, "Post");
        postRelations.Should().NotBeNull();
        postRelations.Should().Contain("ICollection<Tag> Tags");

        // EntityConfiguration is now generated at host level only (DbContextFeature)
        var postConfig = FindConfigurationSource(result, "Post");
        postConfig.Should().BeNull("EntityConfiguration is generated at host level, not module level");
    }

    [Fact]
    public void OneToOne_Dependent_GeneratesFKAndNav()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToOne<Principal>.WithNavigation("Principal", Inverse = "Dependent")]
            public partial class Dependent
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.OneToOne<Dependent>.WithNavigation("Dependent", Inverse = "Principal", IsPrincipal = true)]
            public partial class Principal
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        // Dependent should get FK + ref nav
        var depRelations = FindRelationsSource(result, "Dependent");
        depRelations.Should().NotBeNull("Dependent.Relations.g.cs should be generated");
        depRelations.Should().Contain("PrincipalId");
        depRelations.Should().Contain("Principal Principal");

        // EntityConfiguration is now generated at host level only (DbContextFeature)
        var depConfig = FindConfigurationSource(result, "Dependent");
        depConfig.Should().BeNull("EntityConfiguration is generated at host level, not module level");
    }

    [Fact]
    public void OneToOne_Principal_GeneratesNavOnly()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            [Relation.OneToOne<Detail>.WithNavigation("Detail", Inverse = "Header", IsPrincipal = true)]
            public partial class Header
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.OneToOne<Header>.WithNavigation("Header", Inverse = "Detail")]
            public partial class Detail
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        // Principal side should get ref nav but NO FK
        var headerRelations = FindRelationsSource(result, "Header");
        headerRelations.Should().NotBeNull("Header.Relations.g.cs should be generated");
        headerRelations.Should().Contain("Detail Detail");
        headerRelations.Should().NotContain("DetailId", "Principal side should not have FK");

        // Principal config should NOT contain HasForeignKey (skipped via IsPrincipal check)
        var headerConfig = FindConfigurationSource(result, "Header");
        if (headerConfig is not null)
        {
            headerConfig.Should().NotContain("HasForeignKey",
                "Principal side should not generate FK configuration");
        }
    }

    [Fact]
    public void MultipleRelations_SameTarget_WithNavigation_Disambiguates()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            public partial class User
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.ManyToOne<User>.WithNavigation("CreatedBy", ForeignKey = "CreatedById")]
            [Relation.ManyToOne<User>.WithNavigation("AssignedTo", ForeignKey = "AssignedToId")]
            public partial class Task
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var taskRelations = FindRelationsSource(result, "Task");
        taskRelations.Should().NotBeNull("Task.Relations.g.cs should be generated");

        // Should have two separate FK+nav pairs
        taskRelations.Should().Contain("CreatedById");
        taskRelations.Should().Contain("User CreatedBy");
        taskRelations.Should().Contain("AssignedToId");
        taskRelations.Should().Contain("User AssignedTo");
    }

    /// <summary>
    ///     ⚠️ Optional means nullable on <b>both</b> sides.
    /// </summary>
    /// <remarks>
    ///     A nullable foreign key beside a navigation declared
    ///     <c>public Category Category { get; set; } = null!;</c> — a nullable column and a property
    ///     promising never to be null — is the contradiction, not the contract. Assigning null to it,
    ///     which is what <c>[ReferenceStrategy(Detach)]</c> does, would be <c>CS8601</c> inside a
    ///     generated file.
    /// </remarks>
    [Fact]
    public void OptionalManyToOne_GeneratesNullableFK()
    {
        var source = """
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            [Entity]
            public partial class Category
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [Relation.ManyToOne<Category>.WithNavigation("Category", Required = false)]
            public partial class Product
            {
                public System.Guid PersistenceId { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var productRelations = FindRelationsSource(result, "Product");
        productRelations.Should().NotBeNull("Product.Relations.g.cs should be generated");

        // FK should be nullable (Guid?)
        productRelations.Should().Contain("Guid?", "Optional ManyToOne should generate nullable FK");
        productRelations.Should().Contain("CategoryId");
        productRelations.Should().Contain("Category? Category",
            "an optional relation's navigation is nullable too, or Detach cannot assign null to it");
    }

    // ==================== Helpers ====================

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source, GetReferences());
    }

    private static MetadataReference[] GetReferences()
    {
        return [
            GeneratorTestHelper.FromType<IEntity>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<EntityAttribute>(),
        ];
    }

    /// <summary>
    ///     Finds the generated .Relations.g.cs source for a given entity name.
    ///     Searches all generated sources for one whose hint name contains the entity name and "Relations".
    /// </summary>
    private static string? FindRelationsSource(SourceGenRunResult result, string entityName)
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        foreach (var (hintName, content) in sources)
        {
            if (hintName.Contains(entityName) && hintName.Contains("Relations"))
                return content;
        }

        return null;
    }

    /// <summary>
    ///     Finds the generated .EntityConfig.g.cs source for a given entity name.
    /// </summary>
    private static string? FindConfigurationSource(SourceGenRunResult result, string entityName)
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        foreach (var (hintName, content) in sources)
        {
            if (hintName.Contains(entityName) && hintName.Contains("EntityConfig"))
                return content;
        }

        return null;
    }
}
