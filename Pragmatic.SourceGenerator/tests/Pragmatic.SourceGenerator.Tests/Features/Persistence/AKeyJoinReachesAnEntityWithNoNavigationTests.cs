using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[Join&lt;T&gt;(ForeignKey = …, TargetKey = …)]</c> is a real join, and its target's
///     columns reach the result.
/// </summary>
/// <remarks>
///     <para>
///         Every argument of <c>[Join&lt;T&gt;]</c> reaches the generated query.
///         <c>ForeignKey</c>/<c>TargetKey</c> produce the join, not a comment where the join would go,
///         and a template reads <c>Type</c> — otherwise <c>Left</c> would behave as <c>Inner</c> and
///         silently drop the rows it is written to keep.
///     </para>
///     <para>
///         What does a declared join say that a projection does not? Two things, and only two. An entity reachable by key and by <b>no
///         navigation</b> — where <c>Include</c> cannot be written at all — and an <b>outer</b> join,
///         whose unmatched rows have to survive. Both need the target's columns in the result, so the
///         join owns the whole step: it generates <c>Aggregate</c>, the one member of
///         <c>IQuery&lt;TEntity, TResult&gt;</c> whose shape is filtered-set in, projected-set out.
///     </para>
///     <para>
///         ⚠️ The target's <c>IQueryable</c> cannot be reached from <c>Apply</c>: its parameter is the
///         root's set and there is no navigation to follow. It arrives through
///         <c>IJoiningQuery.BindJoinSources</c>, which the executor calls before it reads
///         <c>Aggregate</c>, from typed <c>IReadRepository&lt;T&gt;</c> instances — no lookup by name
///         and nothing resolved from a string.
///     </para>
/// </remarks>
public class AKeyJoinReachesAnEntityWithNoNavigationTests
{
    [Fact]
    public void AKeyJoin_GeneratesARealJoin_AndNoComment()
    {
        var source = Generated("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        source.Should().Contain(".Join(",
            "a key join is a join, not a comment saying it is not one");
        source.Should().NotContain("Key-based joins require explicit LINQ Join()",
            "the note telling the author to write it by hand was the whole of the feature");

        // ⚠️ The step's carrier is reached through the lambda's parameter; written bare, the member is
        // CS0103 in a file the author did not write. The Left branch qualifies it on its own path, so
        // a declaration with `Type = JoinType.Left` never compiles this one — only this case does.
        source.Should().Contain("(__c, __t0) => new { __c.__r, __t0 }",
            "inside the result selector the carrier is __c, and a bare __r names nothing");
    }

    /// <summary>
    ///     The target's columns reach the result — which is the half that makes a join worth its name.
    /// </summary>
    /// <remarks>
    ///     Without it a key join is a semi-join: it narrows the root set and the caller still cannot
    ///     read one field of what was joined, so <c>[Join&lt;T&gt;]</c> stays a synonym of a
    ///     <c>Where</c>.
    /// </remarks>
    [Fact]
    public void TheJoinedEntitysColumnsReachTheResult()
    {
        var source = Generated("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        source.Should().Contain("Aggregate",
            "one entity in and one result out cannot carry a second source, so the join owns the step");
        source.Should().Contain("CustomerName =",
            "OrderRow.CustomerName names Customer.Name, which no navigation reaches");
    }

    /// <summary>
    ///     A left join keeps the row whose target is missing.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The control is the paragraph below, not this assertion: <c>GroupJoin</c> appears here and
    ///     nowhere in the <c>Inner</c> spelling, and that difference is the whole of <c>Type</c>.
    /// </remarks>
    [Fact]
    public void ALeftJoinGroupJoins_AndAnInnerOneDoesNot()
    {
        var left = Generated("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id", Type = JoinType.Left)]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        left.Should().Contain("GroupJoin(").And.Contain("DefaultIfEmpty()",
            "an order with no customer has to survive, which is the only thing a left join says");

        var inner = Generated("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        inner.Should().NotContain("GroupJoin(",
            "the control: if every join group-joined, Type would be inert in the other direction");
    }

    /// <summary>
    ///     The join's source is handed over, typed, before the projection is built.
    /// </summary>
    [Fact]
    public void TheJoinSourceIsBoundThroughTheContract()
    {
        var source = Generated("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        source.Should().Contain("IJoiningQuery",
            "Apply's parameter is the root's set, and no navigation leads to the target");
        source.Should().Contain("ForBoundary<global::Joins.OrderBoundary>()",
            "EF Core composes a join only inside one DbContext instance, and a host builds one per "
            + "boundary — so the target is read from the ROOT's context, never the target owner's");
        source.Should().Contain("Of<global::Joins.Customer>()",
            "the type argument is written by the generator, which knows it — nothing is looked up by "
            + "name at run time");
    }

    /// <summary>
    ///     The generated join names the column, not the alias the author wrote.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>Id</c> on an <c>[Entity]</c> is an unmapped alias — <c>Id =&gt; PersistenceId</c> — so
    ///     an expression naming it is one EF Core cannot translate. And <c>TargetKey</c> defaults to
    ///     <c>"Id"</c>, which means every key join written the ordinary way went through it. Measured
    ///     on the Showcase before this: the join generated cleanly, compiled cleanly, and every
    ///     request to the endpoint answered <b>500</b>.
    /// </remarks>
    [Fact]
    public void TheDefaultTargetKeyNamesTheColumn_NotTheAlias()
    {
        var source = Generated("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        source.Should().Contain("__t0_k.PersistenceId",
            "Id is a get-only alias with no column behind it, and EF Core translates neither it nor "
            + "anything built on it");
        source.Should().NotContain("__t0_k.Id",
            "the control: the author writes Id because that is what they see, and the generated file "
            + "is where it becomes the column");
    }

    /// <summary>
    ///     <c>PRAG0703</c> does not report the options that work.
    /// </summary>
    /// <remarks>
    ///     What is left of the diagnostic is the shapes that genuinely cannot be generated, which
    ///     <see cref="AJoinTypeThatCannotBeGeneratedIsRefused" /> covers.
    /// </remarks>
    [Fact]
    public void TheOptionsThatNowWork_AreNoLongerReportedInert()
    {
        var result = Run("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id", Type = JoinType.Left)]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0703").Should().BeFalse(
            "ForeignKey, TargetKey and Type all generate now; a warning that survives its cause is "
            + "noise the next author learns to scroll past");
    }

    /// <summary>
    ///     What EF Core cannot express is refused, not quietly turned into something else.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>Full</c> has no LINQ spelling EF Core translates to a FULL OUTER JOIN. Generating an
    ///     inner join for it would be the original defect with a different name, so it is an error the
    ///     author reads at their own declaration.
    /// </remarks>
    [Fact]
    public void AJoinTypeThatCannotBeGeneratedIsRefused()
    {
        var result = Run("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id", Type = JoinType.Full)]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0741").Should().BeTrue(
            "EF Core has no FULL OUTER JOIN, and an inner join wearing its name is the defect this "
            + "story exists to remove");
    }

    /// <summary>
    ///     A result property that neither side can answer is an error at the declaration.
    /// </summary>
    [Fact]
    public void AResultPropertyNeitherSideCanAnswer_IsReported()
    {
        var result = Run("""
            [Query<Order, OrderRowWithStray>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0740").Should().BeTrue(
            "the alternative is a CS0117 inside a .g.cs, at a line the author did not write");
    }

    /// <summary>
    ///     A key on neither side is an error at the declaration, for the same reason.
    /// </summary>
    [Fact]
    public void AKeyThatNamesNoProperty_IsReported()
    {
        var result = Run("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "NoSuchColumn", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0739").Should().BeTrue(
            "ForeignKey is a string the author wrote, resolved where they can see it — the same rule "
            + "Via follows");
    }

    /// <summary>
    ///     A target the boundary cannot reach is refused at the declaration.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         EF Core composes a join only inside one <c>DbContext</c> instance, and a host builds one
    ///         per boundary — so the target has to be in <b>this</b> boundary's model. Without the
    ///         diagnostic the only thing that says so is the first request, with an
    ///         <c>InvalidOperationException</c> naming the <c>[ReadAccess&lt;T&gt;]</c> to add: a
    ///         declaration that compiles and fails at run time.
    ///     </para>
    ///     <para>
    ///         ⚠️ The check belongs in the module, not in the host, even though the boundary's
    ///         <c>DbContext</c> is generated in the host: the model's content is decided by two
    ///         <b>author-written</b> declarations — <c>[Entity]</c>/<c>[BelongsTo]</c> and
    ///         <c>[ReadAccess&lt;T&gt;]</c> on the boundary class — and both live in the module, beside
    ///         the query.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ATargetOutsideTheBoundarysModel_IsReported()
    {
        var result = Run("""
            [Query<Order, OrderRowWithElsewhere>]
            [Join<Elsewhere>(ForeignKey = "CustomerId", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0742").Should().BeTrue(
            "Elsewhere belongs to another boundary and OrderBoundary does not read it, so the set the "
            + "join needs is not in the context the query's own source comes from");

        // ⚠️ One mistake, one message. A refused key join takes the joined step with it, and without
        // the guard the query would fall back to naming {TResult}.Projection — so the author would read
        // PRAG0742, then PRAG0704 about a projection they never asked for, then a CS0117 inside a
        // generated file.
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0704").Should().BeFalse(
            "the query asked for a joined step, not for a projection its result type never declared");

        GeneratorTestHelper.GetGeneratedSource(result, "GetOrders.Query")
            .Should().NotContain("Projection",
                "naming a member the result type does not have is what puts a CS0117 in a file the "
                + "author cannot open");
    }

    /// <summary>
    ///     The first control: a target of the query's own boundary is silent.
    /// </summary>
    /// <remarks>
    ///     Without it, "a target outside the model is refused" is satisfied by refusing every target —
    ///     which would make the whole feature unusable and still pass the case above.
    /// </remarks>
    [Fact]
    public void ATargetOfTheSameBoundary_IsSilent()
    {
        var result = Run("""
            [Query<Order, OrderRow>]
            [Join<Customer>(ForeignKey = "CustomerId", TargetKey = "Id")]
            public partial class GetOrders
            {
                [Filter]
                public string? Reference { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0742").Should().BeFalse(
            "Customer is OrderBoundary's own entity, so it is in the same DbContext by construction");
    }

    /// <summary>
    ///     The second control: <c>[ReadAccess&lt;T&gt;]</c> is what makes the other boundary's entity
    ///     reachable, and the diagnostic has to see it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the half that matters. Without it the diagnostic would refuse exactly the
    ///     declaration it tells the author to write — and its own message names <c>[ReadAccess]</c> as
    ///     the fix, so it would be sending them in a circle.
    /// </remarks>
    [Fact]
    public void ATargetTheBoundaryDeclaresReadAccessTo_IsSilent()
    {
        var result = Run("""
            [Query<Reader, ReaderRow>]
            [Join<Elsewhere>(ForeignKey = "ElsewhereId", TargetKey = "Id")]
            public partial class GetReaders
            {
                [Filter]
                public string? Note { get; init; }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0742").Should().BeFalse(
            "ReaderBoundary declares [ReadAccess<Elsewhere>], which is what puts the DbSet in its own "
            + "context — the very thing the diagnostic asks for");
    }

    private static string Generated(string declarations)
    {
        var source = GeneratorTestHelper.GetGeneratedSource(Run(declarations), "GetOrders.Query");
        source.Should().NotBeNull("a query that declares a join still generates its own file");
        return source!;
    }

    private static SourceGenRunResult Run(string declarations)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            $$"""
            using System;
            using System.Collections.Generic;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;
            using Pragmatic.Mapping.Attributes;

            namespace Joins;

            public sealed class OrderBoundary;

            // ⚠️ No navigation between the two, deliberately: that is the case [Join<T>] answers and
            // the reason [EagerLoad] cannot. Order carries the key and nothing else.
            [Entity]
            [BelongsTo<OrderBoundary>]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
                public Guid CustomerId { get; private set; }
            }

            [Entity]
            [BelongsTo<OrderBoundary>]
            public partial class Customer : IEntity
            {
                public string Name { get; private set; } = "";
            }

            public sealed class OrderRow
            {
                public string Reference { get; init; } = "";
                public string CustomerName { get; init; } = "";
            }

            public sealed class OrderRowWithStray
            {
                public string Reference { get; init; } = "";
                public string Nowhere { get; init; } = "";
            }

            // A second boundary, and an entity of its own: what a key join cannot reach from
            // OrderBoundary, because a host builds one DbContext per boundary.
            public sealed class ElsewhereBoundary;

            [Entity]
            [BelongsTo<ElsewhereBoundary>]
            public partial class Elsewhere : IEntity
            {
                public string Label { get; private set; } = "";
            }

            public sealed class OrderRowWithElsewhere
            {
                public string Reference { get; init; } = "";
                public string ElsewhereLabel { get; init; } = "";
            }

            // A third boundary, which declares the read that makes Elsewhere reachable from it.
            [ReadAccess<Elsewhere>]
            public sealed class ReaderBoundary;

            [Entity]
            [BelongsTo<ReaderBoundary>]
            public partial class Reader : IEntity
            {
                public string Note { get; private set; } = "";
                public Guid ElsewhereId { get; private set; }
            }

            public sealed class ReaderRow
            {
                public string Note { get; init; } = "";
                public string ElsewhereLabel { get; init; } = "";
            }

            {{declarations}}
            """,
            GeneratorTestHelper.FromType<EntityAttribute>(),
            // Without the references the `using`s fail, the whole compilation is an error and the
            // generator emits nothing at all — which reads as "the feature produced nothing" rather
            // than "the harness is short a reference". Measured: the first run of this file generated
            // zero files and zero diagnostics for exactly that.
            GeneratorTestHelper.FromType<global::Pragmatic.Actions.Mutation.EagerLoadAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<global::Pragmatic.Mapping.Attributes.MapFromAttribute<object>>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(global::Pragmatic.Endpoints.Processors.IEndpointPreProcessor)));
}
