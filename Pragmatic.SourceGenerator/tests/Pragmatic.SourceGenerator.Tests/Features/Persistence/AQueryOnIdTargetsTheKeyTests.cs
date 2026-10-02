using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A query filter on the entity's <c>Id</c> targets the key when <c>Id</c> is the generated alias,
///     and stays on <c>Id</c> when the author declared it as a column.
/// </summary>
/// <remarks>
///     The behaviour is proven against Postgres in <c>Conformance.Tests/TheIdAQueryNamesIsTheKey</c>,
///     which answered 500 before (<c>Translation of member 'Id' on entity type 'Order' failed</c>).
///     These cover the half a running application cannot show on one database: the entity whose
///     <c>Id</c> is real must keep its filter where it is.
/// </remarks>
public class AQueryOnIdTargetsTheKeyTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public sealed class SalesBoundary { }

        [PragmaticDbContext("Sales")]
        public partial class SalesDbContext { }
        """;

    private const string GeneratedKey = """

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Code { get; set; } = "";
        }
        """;

    private const string DeclaredKey = """

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public Guid Id { get; set; }
            public Guid PersistenceId { get => Id; set => Id = value; }
            public string Code { get; set; } = "";
        }
        """;

    private const string Queries = """

        [Query<Order, Order>(Single = true)]
        public partial class GetOrderQuery
        {
            public required Guid Id { get; init; }
        }

        [Query<Order, Order>]
        public partial class ListOrdersByIdQuery
        {
            public List<Guid>? Id { get; init; }
        }
        """;

    [Fact]
    public void ASingleQuery_OnTheGeneratedAlias_FiltersThePersistenceId()
    {
        var query = QueryFile(GeneratedKey, "GetOrderQuery");

        query.Should().Contain("e.PersistenceId == this.Id");
        query.Should().NotContain("e.Id == this.Id");
    }

    [Fact]
    public void AListQuery_OnTheGeneratedAlias_FiltersThePersistenceId()
    {
        var query = QueryFile(GeneratedKey, "ListOrdersByIdQuery");

        query.Should().Contain("Contains(e.PersistenceId)");
    }

    /// <summary>The control: an <c>Id</c> the author declared is a column, and the filter stays on it.</summary>
    [Fact]
    public void ASingleQuery_OnADeclaredId_FiltersTheId()
    {
        var query = QueryFile(DeclaredKey, "GetOrderQuery");

        query.Should().Contain("e.Id == this.Id");
        query.Should().NotContain("e.PersistenceId == this.Id");
    }

    private static string QueryFile(string entity, string queryTypeName)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Usings + entity + Queries);

        var file = sources
            .Where(pair => pair.Key.Contains(queryTypeName) && pair.Key.EndsWith(".Query.g.cs"))
            .Select(pair => pair.Value)
            .FirstOrDefault();
        file.Should().NotBeNull($"{queryTypeName} generates its query file");
        return file!;
    }
}
