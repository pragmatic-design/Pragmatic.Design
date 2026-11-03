using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A Pragmatic entity whose tree is a declared relation (<c>[Relation.ManyToOne&lt;Self&gt;.WithNavigation("Parent")]</c>) gets its
///     hierarchy queries on the Sql engine: the navigation is generated, so it is not a member the entity's symbol shows, and the walk
///     goes through the parent key the relation names.
/// </summary>
public class AHierarchyWalksTheDeclaredParentKeyOnSqlTests
{
    private const string Source = """
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Sql;

        namespace Contoso.Catalog
        {
            [Entity]
            /*MARKS*/
            [Relation.ManyToOne<Category>.WithNavigation("Parent", Required = false)]
            [GenerateHierarchy]
            public partial class Category : IEntity
            {
                public string Name { get; private set; } = "";
            }
        }

        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
            public class CompiledSpecification<T> { }
            public class SqlUnitOfWork { }
        }
        """;

    [Theory]
    [InlineData("")]
    [InlineData("[SoftDelete]")]
    [InlineData("[SoftDelete] [Lookup]")]
    public void ADeclaredTree_IsNotPrag3008(string marks)
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Source.Replace("/*MARKS*/", marks), "Pragmatic.Persistence.Sql.Generated", References);

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys.Should().Contain(k => k.Contains("SqlHierarchy"),
            "the declared tree gets its descendants and ancestors on the Sql engine");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG3008").Should().BeFalse(
            "the tree is the declared relation; the Parent navigation is generated, not missing");    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Sql.SqlSession>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}
