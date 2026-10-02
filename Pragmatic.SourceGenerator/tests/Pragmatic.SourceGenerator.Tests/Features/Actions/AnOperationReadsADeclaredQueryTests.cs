using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     <c>[LoadFrom&lt;TQuery&gt;]</c> on a property of an operation: the invoker runs the declared query
///     through the query's own invoker — its validation, its permission, its read — with its inputs bound by
///     name from the operation, and writes the result to the property before the body.
/// </summary>
/// <remarks>
///     An operation that needed data a declared query already publishes invoked it by hand through
///     the boundary's internal facade — which enters an internal call, so the query's permission was not
///     asked — and unwrapped the result: Time off's submission read its balances that way.
/// </remarks>
public class AnOperationReadsADeclaredQueryTests
{
    private const string Model = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Result;

        namespace TestApp
        {
            [Boundary]
            public partial class SalesBoundary { }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public string Reference { get; set; } = "";
                public decimal Total { get; set; }
            }

            [PragmaticDbContext("Sales")]
            public partial class SalesDbContext { }

            [Query<Order, Order>]
            public partial class OrdersByReferenceQuery
            {
                [Filter]
                public required string Reference { get; init; }
            }

            [Query<Order, Order>(Single = true)]
            public partial class OrderByReferenceQuery
            {
                [Filter]
                public required string Reference { get; init; }
            }
        }
        """;

    private static string Operation(string members, string route = "api/summaries") => Model + $$"""

        namespace TestApp
        {
            [DomainAction]
            [Endpoint(HttpVerb.Post, "{{route}}")]
            public partial class SummarizeAction : DomainAction<int>
            {
                {{members}}

                public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<int, IError>>(0);
            }
        }
        """;

    private const string ReadsTheOrders = """
        public required string Reference { get; init; }

        [LoadFrom<OrdersByReferenceQuery>]
        public IReadOnlyList<Order> Orders { get; private set; } = [];
        """;

    [Fact]
    public void TheQueryRuns_ThroughItsOwnInvoker_WithItsInputsBoundByName()
    {
        var invoker = Invoker(Operation(ReadsTheOrders));

        invoker.Should().Contain(
            "new global::TestApp.OrdersByReferenceQuery.Invoker(ServiceProvider).RunAsync(new global::TestApp.OrdersByReferenceQuery { Reference = action.Reference }, ct)",
            "the query's own invoker — its permission, its read — not the internal facade, which skips the permission");
        invoker.Should().Contain("action.Orders = ");
    }

    [Fact]
    public void AFailedQuery_FailsTheOperation_WithItsError()
    {
        var invoker = Invoker(Operation(ReadsTheOrders));

        invoker.Should().Contain("if (__orders.IsFailure)");
        invoker.Should().Contain("return __orders.Error;");
    }

    /// <summary>
    ///     The property is written by the invoker, not sent: the boundary overload has no parameter for it,
    ///     and — the control — the property without the attribute is still an input.
    /// </summary>
    [Fact]
    public void TheProperty_IsNoInput_AndTheOthersStillAre()
    {
        var (sources, _) = TraitCompilationHarness.Generate(Operation(ReadsTheOrders));
        var definition = sources.First(s => s.Key.Contains("_Boundary.") && s.Key.Contains("Definition")).Value;

        var overload = definition.Split('\n').First(l => l.Contains(" Summarize(") && !l.Contains("SummarizeAction action"));
        overload.Should().Contain("reference", "a property without the attribute is still an input");
        overload.Should().NotContain("orders");
    }

    [Theory]
    [InlineData(ReadsTheOrders)]
    [InlineData("""
        public required string Reference { get; init; }

        [LoadFrom<OrderByReferenceQuery>]
        public Order? Order { get; private set; }
        """)]
    public void TheReadingOperation_Compiles(string members)
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Operation(members),
            static path => path.Contains("SummarizeAction") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    [Fact]
    public void ARequiredInputThatBindsNothing_IsReported()
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Operation("""
            public required string Code { get; init; }

            [LoadFrom<OrdersByReferenceQuery>]
            public IReadOnlyList<Order> Orders { get; private set; } = [];
            """));

        diagnostics.Where(d => d.Id == "PRAG0459").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("SummarizeAction") && m.Contains("'Reference'"));
    }

    [Theory]
    [InlineData("[LoadFrom<OrdersByReferenceQuery>]\npublic Order? Orders { get; private set; }")]
    [InlineData("[LoadFrom<Order>]\npublic IReadOnlyList<Order> Orders { get; private set; } = [];")]
    public void APropertyThatIsNotTheQuerysResult_IsReported(string loaded)
    {
        var (_, diagnostics) = TraitCompilationHarness.Generate(Operation(
            "public required string Reference { get; init; }\n\n" + loaded));

        diagnostics.Where(d => d.Id == "PRAG0458").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("SummarizeAction") && m.Contains("Orders"));
    }

    private static string Invoker(string source)
    {
        var (sources, _) = TraitCompilationHarness.Generate(source);
        var match = sources.FirstOrDefault(s => s.Key.Contains("SummarizeAction.Invoker"));
        match.Value.Should().NotBeNull($"the invoker is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
