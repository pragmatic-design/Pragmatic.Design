using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A module declares a <c>[RollUp]</c>; the host has to call the registration that carries it.
/// </summary>
/// <remarks>
///     <para>
///         Registered only from <c>RegisterRollUpRules</c>, a partial hook invoked by
///         <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;()</c> — the per-assembly entry point
///         an application calls itself, and which the Pragmatic host path does not call — the rules
///         never reach the interceptor: it is wired correctly and receives an empty list, and every
///         <c>[RollUp]</c> in a generated application is inert: <c>Sum</c> as much as <c>Count</c>, an
///         aggregate that stays at its default with no error anywhere.
///     </para>
///     <para>
///         It is the same defect the <c>Redaction</c> metadata category exists to prevent: the host
///         learns what to call from metadata entries, so a registration without a category is never
///         called. <c>MetadataCategory.RollUpRules</c> is that category for roll-ups.
///     </para>
///     <para>
///         Two assemblies, because a single compilation cannot show it: the host learns what to call
///         from the metadata attribute a <b>referenced</b> module publishes.
///     </para>
/// </remarks>
public class RollUpRulesReachTheHostTests
{
    private const string ModuleWithARollUp = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Billing;

        [Boundary]
        public partial class BillingBoundary;

        [Entity]
        [BelongsTo<BillingBoundary>]
        [Relation.OneToMany<Line>]
        public partial class Invoice : IEntity
        {
            [RollUp<Line>(nameof(Line.Amount))]
            public decimal Subtotal { get; private set; }

            [RollUp<Line>("", RollUpAggregation.Count)]
            public int LineCount { get; private set; }
        }

        [Entity]
        [BelongsTo<BillingBoundary>]
        [Relation.ManyToOne<Invoice>]
        public partial class Line : IEntity
        {
            public decimal Amount { get; private set; }
        }
        """;

    /// <summary>The same module with the two <c>[RollUp]</c> properties removed.</summary>
    private const string ModuleWithoutARollUp = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Billing;

        [Boundary]
        public partial class BillingBoundary;

        [Entity]
        [BelongsTo<BillingBoundary>]
        [Relation.OneToMany<Line>]
        public partial class Invoice : IEntity
        {
            public decimal Subtotal { get; private set; }
        }

        [Entity]
        [BelongsTo<BillingBoundary>]
        [Relation.ManyToOne<Invoice>]
        public partial class Line : IEntity
        {
            public decimal Amount { get; private set; }
        }
        """;

    /// <summary>
    ///     A host, which is what the marker type makes it: the generator emits the wiring only for a
    ///     compilation that references the Composition host, and the test project does not, so the
    ///     marker is declared here — the same stub <c>PrivacyHostWiringTests</c> uses.
    /// </summary>
    private const string Host = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }

        namespace AppHost
        {
            public static class Program
            {
                public static void Main() { }
            }
        }
        """;

    /// <summary>The module publishes a registration the host can find.</summary>
    /// <remarks>
    ///     The first link of the chain. Without it, "the host does not call it" cannot be told apart
    ///     from "the module never offered anything to call".
    /// </remarks>
    [Fact]
    public void TheModule_PublishesARollUpRegistration()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            ModuleWithARollUp, References);

        var registration = GeneratorTestHelper.GetGeneratedSource(result, "RollUp.Registration");

        registration.Should().NotBeNull("the module declares two roll-ups");
        registration!.Should().Contain("AddGeneratedRollUpRules",
            "a uniquely named public entry point is what a host can call — a partial hook in a class "
            + "every module names identically is not");
    }

    /// <summary>And the host calls it.</summary>
    [Fact]
    public void TheHost_CallsTheModulesRollUpRegistration()
    {
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Billing.Module", ModuleWithARollUp, References);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Host, [..References, module]);

        var services = GeneratorTestHelper.GetGeneratedSource(result, "Host.Services");

        services.Should().NotBeNull("the host wires the services it discovered");
        services!.Should().Contain("AddGeneratedRollUpRules",
            "the interceptor is wired with sp.GetServices<RollUpRule>(), so nothing registering the "
            + "rules means an empty list and an aggregate that never moves");
    }

    /// <summary>
    ///     The control case: the same two assemblies with no <c>[RollUp]</c> anywhere, and the host says
    ///     nothing. Without it "the host calls it" is satisfied by a host that calls it unconditionally,
    ///     which is the shape the assertion above cannot tell apart on its own.
    /// </summary>
    [Fact]
    public void AHostWithNoRollUpAnywhere_CallsNothing()
    {
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Billing.Module", ModuleWithoutARollUp, References);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Host, [..References, module]);

        var services = GeneratorTestHelper.GetGeneratedSource(result, "Host.Services");

        services.Should().NotBeNull("the host is still a host");
        services!.Should().NotContain("AddGeneratedRollUpRules");
        services.Should().NotContain("PragmaticRollUpRuleRegistration");
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.ReadAccessAttribute<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.ComponentModel.Annotations",
            "System.Linq.Queryable",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Actions"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
