using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     Integration tests for the [Projectable] source generator pipeline.
///     Runs the actual PragmaticSourceGenerator on source code with [Projectable] attributes
///     and verifies the generated Expr class output.
/// </summary>
public class ProjectableGeneratorTests
{
    [Fact]
    public void SimpleProjectable_GeneratesExprClass()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public partial class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal SubTotal { get; set; }
                public decimal Tax { get; set; }

                [Projectable]
                public decimal Total => SubTotal + Tax;
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projectable");
        generated.Should().NotBeNull();
        generated.Should().Contain("public static class Expr");
        generated.Should().Contain("Total => e => e.SubTotal + e.Tax");
    }

    [Fact]
    public void MultipleProjectableProperties_GeneratesSingleExprClass()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public partial class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal SubTotal { get; set; }
                public decimal Tax { get; set; }
                public decimal Discount { get; set; }

                [Projectable]
                public decimal Total => SubTotal + Tax;

                [Projectable]
                public decimal NetTotal => SubTotal + Tax - Discount;
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projectable");
        generated.Should().NotBeNull();
        generated.Should().Contain("Total => e => e.SubTotal + e.Tax");
        generated.Should().Contain("NetTotal => e => e.SubTotal + e.Tax - e.Discount");
    }

    [Fact]
    public void StringProjectable_GeneratesCorrectExpression()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public partial class Person : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string FirstName { get; set; } = "";
                public string LastName { get; set; } = "";

                [Projectable]
                public string FullName => FirstName + " " + LastName;
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projectable");
        generated.Should().NotBeNull();
        generated.Should().Contain("FullName => e => e.FirstName + \" \" + e.LastName");
    }

    [Fact]
    public void BoolProjectable_GeneratesComparisonExpression()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public partial class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal Total { get; set; }

                [Projectable]
                public bool IsExpensive => Total > 1000;
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projectable");
        generated.Should().NotBeNull();
        generated.Should().Contain("IsExpensive => e => e.Total > 1000");
    }

    [Fact]
    public void NonPartialClass_GeneratesNothing()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal SubTotal { get; set; }
                public decimal Tax { get; set; }

                [Projectable]
                public decimal Total => SubTotal + Tax;
            }
            """;

        var result = RunGenerator(source);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var projectableSource = sources.Values.FirstOrDefault(s => s.Contains("public static class Expr"));
        projectableSource.Should().BeNull();
    }

    [Fact]
    public void NonExpressionBodied_GeneratesNothing()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public partial class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal SubTotal { get; set; }
                public decimal Tax { get; set; }

                [Projectable]
                public decimal Total { get { return SubTotal + Tax; } }
            }
            """;

        var result = RunGenerator(source);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var projectableSource = sources.Values.FirstOrDefault(s => s.Contains("public static class Expr"));
        projectableSource.Should().BeNull();
    }

    [Fact]
    public void InternalEntity_GeneratesInternalPartialClass()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            internal partial class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal SubTotal { get; set; }
                public decimal Tax { get; set; }

                [Projectable]
                public decimal Total => SubTotal + Tax;
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projectable");
        generated.Should().NotBeNull();
        generated.Should().Contain("internal partial class Order");
    }

    [Fact]
    public void Projectable_GeneratesCorrectHintName()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace MyApp.Sales;

            public partial class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal SubTotal { get; set; }
                public decimal Tax { get; set; }

                [Projectable]
                public decimal Total => SubTotal + Tax;
            }
            """;

        var result = RunGenerator(source);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k => k.Contains("Order") && k.Contains("Projectable"));
    }

    [Fact]
    public void MethodCallInExpression_PreservesNonMemberCalls()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public partial class Product : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal Price { get; set; }

                [Projectable]
                public decimal RoundedPrice => System.Math.Round(Price, 2);
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projectable");
        generated.Should().NotBeNull();
        // Math.Round should not be prefixed, but Price should be
        generated.Should().Contain("e.Price");
        generated.Should().Contain("Math.Round");
    }

    /// <summary>
    ///     A navigation the relation declares is a member the generator adds, so the transform's semantic
    ///     model cannot bind it. It is still the entity's, and the expression reads it through the
    ///     parameter — while <c>l.Id</c>, a member of the line with a name the order also has, stays the
    ///     line's.
    /// </summary>
    [Fact]
    public void ANavigationDeclaredByARelation_IsReadThroughTheParameter()
    {
        var source = """
            using System.Linq;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [Entity]
            [Relation.OneToMany<OrderLine>.WithNavigation("Lines", Inverse = "Order")]
            public partial class Order : IEntity
            {
                public decimal Discount { get; private set; }

                [Projectable]
                public decimal Total => Lines.Where(l => l.Id != Id).Sum(l => l.Amount) - Discount;
            }

            [Entity]
            [Relation.ManyToOne<Order>.WithNavigation("Order", Inverse = "Lines")]
            public partial class OrderLine : IEntity
            {
                public decimal Amount { get; private set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Order.Projectable");
        generated.Should().Contain("Total => e => e.Lines.Where(l => l.Id != e.Id).Sum(l => l.Amount) - e.Discount");
    }

    /// <summary>A trait member is generated too: <c>[Auditable]</c> adds <c>CreatedAt</c>.</summary>
    [Fact]
    public void ATraitMember_IsReadThroughTheParameter()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            [Entity]
            [Auditable]
            public partial class Order : IEntity
            {
                public System.DateTimeOffset? ShippedAt { get; private set; }

                [Projectable]
                public bool ShippedLate => ShippedAt > CreatedAt.AddDays(2);
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Order.Projectable");
        generated.Should().Contain("ShippedLate => e => e.ShippedAt > e.CreatedAt.AddDays(2)");
    }

    /// <summary>
    ///     A projectable member that names another is given the other's body: the getter of a computed
    ///     member is no column, and an expression that reads it cannot be translated.
    /// </summary>
    [Fact]
    public void AProjectableNamingAnother_InlinesItsBody()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public partial class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public decimal SubTotal { get; set; }
                public decimal Tax { get; set; }

                [Projectable]
                public decimal Total => SubTotal + Tax;

                [Projectable]
                public bool IsExpensive => Total > 1000;
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projectable");
        generated.Should().Contain("IsExpensive => e => (e.SubTotal + e.Tax) > 1000");
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GetPersistenceReferences());
    }

    private static MetadataReference[] GetPersistenceReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<IEntity>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<ProjectableAttribute>(),
        ];
    }
}
