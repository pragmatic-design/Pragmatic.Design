using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>[WithoutFilter&lt;TEntity&gt;]</c> on a <c>[SoftDelete]</c> entity reaches its
///     soft-deleted rows.
/// </summary>
/// <remarks>
///     The attribute disabled the entity's Pragmatic filters and nothing else. A soft-deletable entity
///     also carries an EF Core named filter, <c>"SoftDelete"</c>, that only a lifted name gets past — so an
///     operation declaring the attribute still could not load a row that had been soft-deleted, which is
///     what it was declared for. Found by Time off: erasing an employee who has left answered 404.
/// </remarks>
public class WithoutFilterOnASoftDeletableEntityTests
{
    private static string Source(string entityAttributes) => $$"""
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Filters;

        namespace Sales
        {
            [Entity]
            {{entityAttributes}}
            public partial class Order : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Reference { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Update)]
            [WithoutFilter<Order>]
            public partial class AmendOrderMutation : Mutation<Order>
            {
                public required Guid Id { get; init; }
                public string Reference { get; init; } = "";
            }
        }
        """;

    private static string Invoker(string entityAttributes)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(entityAttributes), [
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Query.Filters.IQueryFilterToggle>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Actions.Mutation.Mutation<>)),
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        ]);

        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(pair => pair.Key.EndsWith("AmendOrderMutation.MutationInvoker.g.cs")).Value;
    }

    /// <summary>The soft-delete filter is lifted by its name, beside the entity's own filters.</summary>
    [Fact]
    public void ASoftDeletableEntity_LiftsItsSoftDeleteFilterToo()
    {
        var invoker = Invoker("[SoftDelete]");

        invoker.Should().Contain("Disable(typeof(global::Sales.Order))");
        invoker.Should().Contain("DisableQueryFilter(\"SoftDelete\")",
            "the EF named filter is lifted only by name, and without it the soft-deleted row stays hidden");
    }

    /// <summary>The control: an entity that is not soft-deletable has no such filter to lift.</summary>
    [Fact]
    public void AnEntityThatIsNotSoftDeletable_LiftsNoName()
    {
        var invoker = Invoker("");

        invoker.Should().Contain("Disable(typeof(global::Sales.Order))");
        invoker.Should().NotContain("DisableQueryFilter(");
    }
}
