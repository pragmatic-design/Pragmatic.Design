using System.Collections.Generic;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     Verifies #2 roll-up codegen: the parent's internal apply method and the typed rule registration that
///     wires <c>[RollUp&lt;Line&gt;(nameof(Line.Amount))]</c> on <c>Invoice.Subtotal</c>.
/// </summary>
public class RollUpTemplateTests
{
    private static readonly RollUpModel Subtotal = new()
    {
        ParentFullName = "global::App.Billing.Invoice",
        ParentShortName = "Invoice",
        ParentNamespace = "App.Billing",
        RollupProperty = "Subtotal",
        ChildFullName = "global::App.Billing.Line",
        ChildShortName = "Line",
        ChildAmountProperty = "Amount",
        ChildForeignKeyProperty = "InvoiceId",
        RollupPropertyTypeName = "decimal"
    };

    /// <summary>The other member of the enum: a count, which must not be read as a sum.</summary>
    private static readonly RollUpModel LineCount = new()
    {
        ParentFullName = "global::App.Billing.Invoice",
        ParentShortName = "Invoice",
        ParentNamespace = "App.Billing",
        RollupProperty = "LineCount",
        ChildFullName = "global::App.Billing.Line",
        ChildShortName = "Line",
        ChildAmountProperty = "",
        ChildForeignKeyProperty = "InvoiceId",
        IsCount = true,
        RollupPropertyTypeName = "int"
    };

    /// <summary>A count lands in an <c>int</c>, and the conversion is where both types are known.</summary>
    [Fact]
    public void EntityTemplate_ConvertsTheDelta_ForANonDecimalAggregate()
    {
        var source = new RollUpEntityTemplate("Invoice", "App.Billing", [LineCount])
            .RenderOutput().Text;

        source.Should().Contain("internal void __ApplyRollUp_LineCount(decimal delta) => LineCount += (int)delta;");
    }

    /// <summary>And its rule reads a constant rather than a child property.</summary>
    [Fact]
    public void RegistrationTemplate_ReadsOnePerChild_ForACount()
    {
        var source = new RollUpRegistrationTemplate([LineCount], "App.Billing").RenderOutput().Text;

        source.Should().Contain("Amount = c => 1m,");
        source.Should().NotContain("Amount = c => c.",
            "naming a child property for a count is what produced the CS0029");
    }

    [Fact]
    public void EntityTemplate_GeneratesInternalApplyMethod_OnParentPartial()
    {
        var source = new RollUpEntityTemplate("Invoice", "App.Billing", [Subtotal])
            .RenderOutput().Text;

        source.Should().Contain("namespace App.Billing");
        source.Should().Contain("partial class Invoice");
        source.Should().Contain("internal void __ApplyRollUp_Subtotal(decimal delta) => Subtotal += delta;");
    }

    [Fact]
    public void RegistrationTemplate_EmitsAPublicEntryPoint_WithTypedRule()
    {
        var source = new RollUpRegistrationTemplate([Subtotal], "App.Billing").RenderOutput().Text;

        source.Should().Contain("namespace App.Billing.Generated;");
        source.Should().Contain("public static class PragmaticRollUpRuleRegistration");
        source.Should().Contain("AddGeneratedRollUpRules");
        source.Should().Contain("new global::Pragmatic.Persistence.RollUp.RollUpRule<global::App.Billing.Line, global::App.Billing.Invoice>");
        source.Should().Contain("AggregatePropertyName = \"Subtotal\",");
        source.Should().Contain("ParentKey = c => c.InvoiceId,");
        source.Should().Contain("Amount = c => c.Amount,");
        source.Should().Contain("ApplyToParent = (p, d) => p.__ApplyRollUp_Subtotal(d)");
    }

    /// <summary>
    ///     The hand-wired path keeps working: the hook is still implemented, in a file of its own
    ///     because two file-scoped namespaces in one file is a CS8954.
    /// </summary>
    [Fact]
    public void HookTemplate_DelegatesToTheEntryPoint_InItsOwnFile()
    {
        var artifact = new RollUpHookTemplate("App.Billing").RenderOutput();

        artifact.HintName.Should().Be("_Infra.RollUp.Hook.g.cs");
        artifact.Text.Should().Contain("namespace Pragmatic.Persistence.Generated;");
        artifact.Text.Should().Contain("public static partial class PragmaticPersistenceRegistration");
        artifact.Text.Should().Contain("static partial void RegisterRollUpRules(");
        artifact.Text.Should().Contain(
            "=> global::App.Billing.Generated.PragmaticRollUpRuleRegistration.AddGeneratedRollUpRules(services);");
    }

    [Fact]
    public void RegistrationTemplate_MultipleRollUps_RegistersEach()
    {
        var tax = Subtotal with { RollupProperty = "Tax", ChildAmountProperty = "TaxAmount" };

        var source = new RollUpRegistrationTemplate([Subtotal, tax], "App.Billing").RenderOutput().Text;

        source.Should().Contain("p.__ApplyRollUp_Subtotal(d)");
        source.Should().Contain("p.__ApplyRollUp_Tax(d)");
        source.Should().Contain("Amount = c => c.TaxAmount,");
    }
}
