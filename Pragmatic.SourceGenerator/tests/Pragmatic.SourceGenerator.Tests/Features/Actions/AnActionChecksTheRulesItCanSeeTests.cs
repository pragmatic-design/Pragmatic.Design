using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Actions;

/// <summary>
///     An action that loads an entity checks that entity's <c>[Invariant]</c> rules — but only the ones
///     whose body reads nothing the action left out of its <c>Include</c>.
/// </summary>
/// <remarks>
///     <para>
///         This shape rather than an informational diagnostic. If only the <b>mutation</b> invoker
///         checked invariants, an aggregate moved by a <c>[DomainAction]</c> — which is how a state
///         machine is moved — would have its rules evaluated on no path at all.
///     </para>
///     <para>
///         ⚠️ The naive form is <b>excluded by a measurement</b>, not a preference. Checking every
///         invariant would evaluate them against whatever the action included, and an action includes
///         what its own body needs: voiding an invoice includes <c>Lines</c> and not <c>Payments</c>, so
///         a rule reading <c>Payments</c> would compare a real amount against an empty collection and
///         answer false — a 422 on a request that must succeed. Invoicing's
///         <c>VoidingAPartlyPaidInvoice_IsAllowed</c> is that request.
///     </para>
///     <para>
///         So the rule is: check what the action can see. What it cannot see is skipped, which fails
///         <b>closed</b> — a rule not evaluated leaves the action as it would be without the check, and a
///         rule evaluated against an empty navigation is a wrong answer.
///     </para>
/// </remarks>
public class AnActionChecksTheRulesItCanSeeTests
{
    private const string Model = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Result;

        namespace TestApp
        {
            [Boundary]
            public partial class BillingBoundary { }

            [Entity]
            [BelongsTo<BillingBoundary>]
            public partial class Payment : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public Guid InvoiceId { get; set; }
                public decimal Amount { get; set; }
            }

            [Entity]
            [BelongsTo<BillingBoundary>]
            public partial class Line : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public Guid InvoiceId { get; set; }
                public decimal Total { get; set; }
            }

            [Entity]
            [BelongsTo<BillingBoundary>]
            [Relation.OneToMany<Payment>.WithNavigation("Payments", Inverse = "Invoice")]
            [Relation.OneToMany<Line>.WithNavigation("Lines", Inverse = "Invoice")]
            public partial class Invoice : IEntity
            {
                public Guid Id { get; set; }
                public Guid PersistenceId { get => Id; set => Id = value; }
                public decimal AmountPaid { get; set; }
                public decimal Charged { get; set; }
                public string? VoidReason { get; set; }

                /// <summary>Reads a navigation: only an action that included Payments can answer it.</summary>
                [Invariant("The amount paid equals the payments recorded")]
                public bool AmountPaidMatchesPayments() => AmountPaid == Payments.Sum(p => p.Amount);

                /// <summary>Reads nothing but its own columns, so every action can answer it.</summary>
                [Invariant("A voided invoice says why")]
                public bool VoidedInvoiceHasAReason() => VoidReason == null || VoidReason.Length > 0;
            }

            [PragmaticDbContext("Billing")]
            public partial class BillingDbContext { }

            // Includes both: it is the operation that writes a payment, and it needs the others to
            // decide. Both rules are answerable here.
            [DomainAction]
            [LoadEntity<Invoice>(nameof(Id), Include = "Lines,Payments", FieldName = "_invoice")]
            public partial class RecordPaymentAction : DomainAction<bool>
            {
                public required Guid Id { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }

            // Includes the lines and not the payments: the rule that reads Payments cannot be answered
            // here, and answering it anyway is the 422 on a legitimate void.
            [DomainAction]
            [LoadEntity<Invoice>(nameof(Id), Include = "Lines", FieldName = "_invoice")]
            public partial class VoidInvoiceAction : DomainAction<bool>
            {
                public required Guid Id { get; init; }

                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }

            // Loads nothing: there is no entity to check.
            [DomainAction]
            public partial class ReportAction : DomainAction<bool>
            {
                public override Task<Result<bool, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult<Result<bool, IError>>(true);
            }
        }
        """;

    /// <summary>An action that included what a rule reads checks that rule.</summary>
    [Fact]
    public void AnActionThatIncludedWhatTheRuleReads_ChecksIt()
    {
        var invoker = Invoker("RecordPaymentAction.Invoker");

        invoker.Should().Contain("AmountPaidMatchesPayments()",
            "Payments is included, so the rule can be answered on this path");
        invoker.Should().Contain("VoidedInvoiceHasAReason()",
            "and a rule that reads no navigation is answerable everywhere");
    }

    /// <summary>
    ///     The control, and the reason the naive form is wrong: the rule that
    ///     reads a navigation the action did not include is <b>not</b> checked there.
    /// </summary>
    [Fact]
    public void AnActionThatDidNotIncludeIt_SkipsThatRuleAndKeepsTheOthers()
    {
        var invoker = Invoker("VoidInvoiceAction.Invoker");

        invoker.Should().NotContain("AmountPaidMatchesPayments()",
            "Payments was not included: the rule would compare a real amount against an empty "
            + "collection and refuse a legitimate void");
        invoker.Should().Contain("VoidedInvoiceHasAReason()",
            "while the rule that reads only columns is still checked — skipping every rule would be "
            + "the old behaviour with more code");
    }

    /// <summary>The second control: an action that loads nothing checks nothing.</summary>
    [Fact]
    public void AnActionThatLoadsNothing_ChecksNothing()
        => Invoker("ReportAction.Invoker").Should().NotContain("Invariant");

    [Fact]
    public void TheOperations_Compile()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Model,
            static path => path.Contains("RecordPaymentAction") || path.Contains("VoidInvoiceAction")
                           || path.Contains("ReportAction") || path.EndsWith("TestSource.cs"));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    private static string Invoker(string hint)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Model);
        var match = sources.FirstOrDefault(s => s.Key.Contains(hint));
        match.Value.Should().NotBeNull($"{hint} is generated; generated: {string.Join(", ", sources.Keys)}");
        return match.Value;
    }
}
