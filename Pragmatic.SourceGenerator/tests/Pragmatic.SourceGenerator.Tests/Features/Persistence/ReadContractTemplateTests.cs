using System.Collections.Generic;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Verifies #3 read-contract codegen: a boundary's <c>[Published]</c> queries produce an
///     <c>I{Module}Reads</c> interface, an implementation that delegates to <c>IQueryExecutor</c>, and a DI
///     registration. One repository field per distinct entity.
/// </summary>
public class ReadContractTemplateTests
{
    private static string Render(params PublishedQueryModel[] queries) =>
        new ReadContractTemplate("IBillingReads", "App.Billing.Contracts", queries)
            .RenderOutput().Text;

    private static PublishedQueryModel Query(string method, string entityShort, string entityFqn, string dtoFqn) =>
        new()
        {
            ContractName = "IBillingReads",
            ContractNamespace = "App.Billing.Contracts",
            MethodName = method,
            QueryTypeShortName = method,
            QueryTypeFullName = $"global::App.Billing.Queries.{method}",
            EntityTypeFullName = entityFqn,
            EntityIdTypeFullName = "global::System.Guid",
            EntityShortName = entityShort,
            ResultTypeFullName = dtoFqn
        };

    private static readonly PublishedQueryModel Invoices =
        Query("GetInvoices", "Invoice", "global::App.Billing.Invoice", "global::App.Billing.Dtos.InvoiceDto");

    private static readonly PublishedQueryModel Payments =
        Query("GetPayments", "Payment", "global::App.Billing.Payment", "global::App.Billing.Dtos.PaymentDto");

    [Fact]
    public void GeneratesInterface_InContractsNamespace_WithMethodPerQuery()
    {
        var source = Render(Invoices, Payments);

        source.Should().Contain("namespace App.Billing.Contracts");
        source.Should().Contain("public interface IBillingReads");
        source.Should().Contain(
            "global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::App.Billing.Dtos.InvoiceDto>> GetInvoices(global::App.Billing.Queries.GetInvoices query, global::System.Threading.CancellationToken ct = default);");
        source.Should().Contain("GetPayments(global::App.Billing.Queries.GetPayments query");
    }

    [Fact]
    public void GeneratesImplementation_DelegatingToQueryExecutor()
    {
        var source = Render(Invoices);

        source.Should().Contain("public sealed class BillingReads : IBillingReads");
        source.Should().Contain(
            "private readonly global::Pragmatic.Persistence.Repository.IReadRepository<global::App.Billing.Invoice> _invoiceRepository;");
        source.Should().Contain("private readonly global::Pragmatic.Persistence.Query.Executors.IQueryExecutor _queryExecutor;");
        source.Should().Contain(
            "=> _queryExecutor.ExecuteAllAsync<global::App.Billing.Invoice, global::App.Billing.Dtos.InvoiceDto>(query, _invoiceRepository.Query(), ct);");
    }

    [Fact]
    public void DistinctEntities_ProduceOneRepositoryFieldEach()
    {
        var source = Render(Invoices, Payments);

        source.Should().Contain("_invoiceRepository");
        source.Should().Contain("_paymentRepository");
    }

    [Fact]
    public void SameEntityTwice_DedupesToOneRepositoryField()
    {
        var anotherInvoiceQuery =
            Query("GetOverdueInvoices", "Invoice", "global::App.Billing.Invoice", "global::App.Billing.Dtos.InvoiceDto");

        var source = Render(Invoices, anotherInvoiceQuery);

        // One field, two methods.
        System.Text.RegularExpressions.Regex.Matches(source, "_invoiceRepository;").Count.Should().Be(1);
        source.Should().Contain("GetInvoices(");
        source.Should().Contain("GetOverdueInvoices(");
    }

    [Fact]
    public void GeneratesDiRegistration()
    {
        var source = Render(Invoices);

        source.Should().Contain("public static class BillingReadsRegistrationExtensions");
        source.Should().Contain("AddScoped<IBillingReads, BillingReads>");
        source.Should().Contain("AddBillingReads");
    }

    /// <summary>
    ///     A query that declares <c>[QueryStrategy]</c> reads through the repository overload that takes
    ///     the strategy, instead of the parameterless one.
    /// </summary>
    /// <remarks>
    ///     The repository has had the strategy switch all along — <c>Query(QueryStrategy)</c> chooses
    ///     tracking and whether the Pragmatic filter pipeline runs. What was missing was the caller: the
    ///     read contract always called <c>Query()</c>, so declaring the strategy changed nothing.
    /// </remarks>
    [Fact]
    public void DeclaredStrategy_ReachesTheRepositoryCall()
    {
        var source = Render(Invoices with { Strategy = QueryStrategyKind.Raw });

        source.Should().Contain(
            "_invoiceRepository.Query(global::Pragmatic.Persistence.Query.QueryStrategy.Raw)");
    }

    [Fact]
    public void NoDeclaredStrategy_KeepsTheParameterlessRepositoryCall()
    {
        var source = Render(Invoices);

        source.Should().Contain("_invoiceRepository.Query(), ct);",
            "the default is unchanged: a query that declares nothing reads the way it always did");
    }

    /// <summary>
    ///     Two queries over the same entity, one with a strategy and one without: the strategy belongs to
    ///     the query, not to the repository field they share.
    /// </summary>
    [Fact]
    public void StrategyIsPerQuery_NotPerRepositoryField()
    {
        var overdue = Query("GetOverdueInvoices", "Invoice", "global::App.Billing.Invoice",
            "global::App.Billing.Dtos.InvoiceDto") with { Strategy = QueryStrategyKind.Projection };

        var source = Render(Invoices, overdue);

        source.Should().Contain("GetInvoices(global::App.Billing.Queries.GetInvoices query");
        source.Should().Contain("_invoiceRepository.Query(), ct);");
        source.Should().Contain(
            "_invoiceRepository.Query(global::Pragmatic.Persistence.Query.QueryStrategy.Projection)");
    }
}
