using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     What a <c>[Published]</c> query is called on the read contract.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It was called after the class, suffix and all, so declaring <c>SearchCategoriesQuery</c>
///         made every consumer of every boundary write
///         <c>SearchCategoriesQuery(new SearchCategoriesQuery { … })</c> — the type name twice on one
///         line. That is the whole public surface of the feature, and the convention everywhere else
///         in this framework strips the suffix.
///     </para>
///     <para>
///         Two ways in, one place that decides: an explicit <c>MethodName</c> read off <c>[Published]</c>
///         exactly as <c>ContractName</c> already is, and otherwise the class name with a trailing
///         <c>Query</c> removed.
///     </para>
/// </remarks>
public class PublishedQueryMethodNameTests
{
    private const string Preamble = """
        using System;
        using System.Linq;
        using System.Linq.Expressions;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace App.Billing.Queries;

        [Entity]
        public partial class Invoice : IEntity
        {
            public string Number { get; set; } = "";
        }

        public class InvoiceDto
        {
            public string Number { get; set; } = "";

            public static Expression<Func<Invoice, InvoiceDto>> Projection =>
                i => new InvoiceDto { Number = i.Number };
        }
        """;

    /// <summary>The default: the class name without its <c>Query</c> suffix.</summary>
    [Fact]
    public void AQueryClassEndingInQuery_ContributesTheNameWithoutIt()
    {
        var contract = ContractFor("""
            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class SearchInvoicesQuery
            {
                public string? Number { get; init; }
            }
            """);

        contract.Should().Contain("> SearchInvoices(global::App.Billing.Queries.SearchInvoicesQuery query")
            .And.NotContain("SearchInvoicesQuery(global::");
    }

    /// <summary>An explicit name wins, and is read where the contract name already is.</summary>
    [Fact]
    public void AnExplicitMethodName_WinsOverTheDefault()
    {
        var contract = ContractFor("""
            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads", MethodName = "FindInvoices")]
            public partial class SearchInvoicesQuery
            {
                public string? Number { get; init; }
            }
            """);

        contract.Should().Contain("> FindInvoices(global::App.Billing.Queries.SearchInvoicesQuery query")
            .And.NotContain("SearchInvoices(");
    }

    /// <summary>
    ///     The control: a class that does not end in <c>Query</c> keeps its whole name.
    /// </summary>
    /// <remarks>
    ///     Without it, "strip the suffix" is satisfied by a rule that trims five characters from
    ///     everything, and <c>GetInvoices</c> would become <c>GetIn</c>.
    /// </remarks>
    [Fact]
    public void AQueryClassNotEndingInQuery_KeepsItsWholeName()
    {
        var contract = ContractFor("""
            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class GetInvoices
            {
                public string? Number { get; init; }
            }
            """);

        contract.Should().Contain("> GetInvoices(global::App.Billing.Queries.GetInvoices query");
    }

    /// <summary>
    ///     The second control: a class named only <c>Query</c> keeps it, because the remainder is empty.
    /// </summary>
    /// <remarks>
    ///     A method with no name is not a name the author can be told to fix; the suffix is dropped only
    ///     when something is left.
    /// </remarks>
    [Fact]
    public void AQueryClassNamedOnlyQuery_KeepsIt()
    {
        var contract = ContractFor("""
            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class Query
            {
                public string? Number { get; init; }
            }
            """);

        contract.Should().Contain("> Query(global::App.Billing.Queries.Query query");
    }

    /// <summary>
    ///     Two queries that would contribute the same method name are reported, not emitted twice.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The case the stripping creates: <c>SearchInvoicesQuery</c> and <c>SearchInvoices</c> are two
    ///     distinct types that, once stripped, want one name. Emitting both would be <c>CS0111</c> inside a generated
    ///     file, which sends the author to code they did not write; <c>PRAG0728</c> names the two
    ///     declarations and the way out.
    /// </remarks>
    [Fact]
    public void TwoQueriesThatWouldShareAMethodName_AreReported()
    {
        var result = Run("""
            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class SearchInvoicesQuery
            {
                public string? Number { get; init; }
            }

            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class SearchInvoices
            {
                public string? Number { get; init; }
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0728").Should().NotBeEmpty();

        var contract = GeneratorTestHelper.GetGeneratedSource(result, "_ReadContract");
        contract.Should().BeNull("a contract that cannot name its methods apart is not emitted at all");
    }

    /// <summary>
    ///     The control for that: two queries whose stripped names differ are not reported.
    /// </summary>
    /// <remarks>
    ///     Without it, "collisions are reported" is satisfied by a generator that reports every contract
    ///     with more than one query in it.
    /// </remarks>
    [Fact]
    public void TwoQueriesWithDistinctNames_AreNotReported()
    {
        var result = Run("""
            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class SearchInvoicesQuery
            {
                public string? Number { get; init; }
            }

            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class CountInvoicesQuery
            {
                public string? Number { get; init; }
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0728").Should().BeEmpty();

        var contract = GeneratorTestHelper.GetGeneratedSource(result, "_ReadContract");
        contract.Should().NotBeNull();
        contract!.Should().Contain("> SearchInvoices(").And.Contain("> CountInvoices(");
    }

    private static SourceGenRunResult Run(string declaration)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Preamble + "\n" + declaration,
            References);

    private static string ContractFor(string declaration)
    {
        var contract = GeneratorTestHelper.GetGeneratedSource(Run(declaration), "_ReadContract");

        contract.Should().NotBeNull(
            "the query carries [Published], so a read contract is generated — without this the "
            + "assertions below would hold on a run that produced nothing");

        return contract!;
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<PublishedAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Result.IError>(),
    ];
}
