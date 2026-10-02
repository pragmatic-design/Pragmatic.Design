using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     End-to-end cover for <c>[QueryStrategy]</c> on a <c>[Published]</c> query: from the attribute in
///     source to the repository call in the generated read contract.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ReadContractTemplateTests" /> asserts the same thing one layer down, on a model
///         built by hand. That test would stay green if <c>PublishedQueryTransform</c> never read the
///         attribute — which is exactly the state this feature was in — so the transform needs its own
///         proof.
///     </para>
/// </remarks>
public class PublishedQueryStrategyTests
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

    private static string ContractFor(string declaration)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Preamble + "\n" + declaration,
            References);

        var contract = GeneratorTestHelper.GetGeneratedSource(result, "_ReadContract");

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

    [Fact]
    public void DeclaredStrategy_ReachesTheGeneratedRepositoryCall()
    {
        var contract = ContractFor("""
            [QueryStrategy(Strategy = QueryStrategy.Raw)]
            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class GetInvoices
            {
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 20;
            }
            """);

        contract.Should().Contain(
            "_invoiceRepository.Query(global::Pragmatic.Persistence.Query.QueryStrategy.Raw)");
    }

    [Fact]
    public void NoDeclaredStrategy_KeepsTheParameterlessRepositoryCall()
    {
        var contract = ContractFor("""
            [Query<Invoice, InvoiceDto>]
            [Published(ContractName = "IBillingReads")]
            public partial class GetInvoices
            {
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 20;
            }
            """);

        contract.Should().Contain("_invoiceRepository.Query(), ct);");
        contract.Should().NotContain("QueryStrategy.");
    }
}
