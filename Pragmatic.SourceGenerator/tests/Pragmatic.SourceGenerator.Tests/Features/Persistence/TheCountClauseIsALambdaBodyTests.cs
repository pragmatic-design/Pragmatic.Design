using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[Count&lt;T&gt;(Where = …)]</c> takes the <b>body of a lambda</b>, and the generator says so
///     instead of leaving a <c>CS0103</c> in a file the author cannot open.
/// </summary>
/// <remarks>
///     <para>
///         The clause is inserted verbatim into <c>g.Count(x =&gt; {clause})</c>, so it has to be written
///         against <c>x</c> — and the attribute's own example read
///         <c>Where = "Status == OrderStatus.Completed"</c>, which becomes
///         <c>x =&gt; Status == OrderStatus.Completed</c>: <c>CS0103</c>, inside
///         <c>{View}.QueryView.g.cs</c>. The example is the first thing an author copies.
///     </para>
///     <para>
///         ⚠️ The trap is the asymmetry inside one family of attributes: <c>Sum</c>, <c>Avg</c>,
///         <c>Min</c> and <c>Max</c> take a bare member path and the generator writes
///         <c>x.{Expression}</c> itself, while <c>Where</c> takes a whole expression and is inserted as
///         written. Same attributes, two conventions, and nothing said which was which.
///     </para>
/// </remarks>
public class TheCountClauseIsALambdaBodyTests
{
    private static string Source(string countAttribute) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Sales
        {
            [Entity]
            public partial class Order : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Region { get; private set; } = "";
                public bool IsCancelled { get; private set; }
                public decimal Total { get; private set; }
            }

            [QueryView<Order>]
            public partial class RegionSummary
            {
                [GroupBy] public string Region { get; init; } = "";

                {{countAttribute}}
                public int CancelledCount { get; init; }
            }
        }
        """;

    private static SourceGenRunResult Run(string countAttribute)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(countAttribute), [
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Query.Attributes.QueryViewAttribute<object>>(),
            GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Specification.Spec<>)),
        ]);

    /// <summary>The documented form is reported, at the declaration.</summary>
    [Fact]
    public void AClauseThatNamesNoRow_IsReported()
    {
        var result = Run("""[Count<Order>(Where = "IsCancelled")]""");

        var diagnostics = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0722").ToList();

        diagnostics.Should().ContainSingle("the clause cannot filter anything: it never mentions the row");
        diagnostics[0].GetMessage().Should().Contain("CancelledCount")
            .And.Contain("x.", "the message has to name the form that works");
    }

    /// <summary>The control: the working form is silent.</summary>
    /// <remarks>
    ///     Without it, "a clause is reported" would be satisfied by reporting every conditional count —
    ///     which would make the diagnostic noise on the one form that is correct.
    /// </remarks>
    [Fact]
    public void TheLambdaBodyForm_IsSilent()
    {
        var result = Run("""[Count<Order>(Where = "x.IsCancelled")]""");

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0722").Should().BeEmpty();
    }

    /// <summary>And a count with no clause at all is not a clause to complain about.</summary>
    [Fact]
    public void ACountWithNoClause_IsSilent()
    {
        var result = Run("[Count<Order>]");

        GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0722").Should().BeEmpty();
    }

    /// <summary>
    ///     The generated view still counts what the clause says, so the diagnostic is a guard and not a
    ///     replacement for the feature.
    /// </summary>
    [Fact]
    public void TheWorkingForm_StillRendersTheCount()
    {
        var view = GeneratorTestHelper.GetGeneratedSource(
            Run("""[Count<Order>(Where = "x.IsCancelled")]"""), "RegionSummary");

        view.Should().NotBeNull();
        view!.Should().Contain("g.Count(x => x.IsCancelled)");
    }
}
