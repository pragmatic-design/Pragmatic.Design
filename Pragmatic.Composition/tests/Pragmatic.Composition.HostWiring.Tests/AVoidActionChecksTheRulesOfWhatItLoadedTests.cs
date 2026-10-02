// Pragmatic.Composition.HostWiring.Tests - The loaded entity's rules on a void action

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A <c>VoidDomainAction</c> that loads an entity carrying <c>[Invariant]</c> compiles, and its invoker
///     checks the rules the way a typed action's does.
/// </summary>
/// <remarks>
///     The generator emits the <c>CheckLoadedInvariants</c> override for every action that loads such an
///     entity, and the void invoker has to have the member to override too, or the void shape is
///     <c>CS0115</c> in a generated file the author cannot edit. No example had a void action loading an
///     entity with a rule, so nothing compiled one. Compiled for real, with the whole reference set.
/// </remarks>
public sealed class AVoidActionChecksTheRulesOfWhatItLoadedTests
{
    private static string Module(string invariant) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace Payments
        {
            [Boundary]
            public partial class PaymentsBoundary;

            [Module(Name = "Payments")]
            public sealed class PaymentsModule;
        }

        namespace Payments.Charges
        {
            [Entity]
            public partial class Charge : IEntity
            {
                public decimal Amount { get; private set; }

                {{invariant}}
            }

            [DomainAction]
            [LoadEntity<Charge>(nameof(Id))]
            public partial class SettleCharge : VoidDomainAction
            {
                public required Guid Id { get; init; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
        }
        """;

    private const string Host = """
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Payments;

        namespace Payments.Host;

        [Module]
        [Include<PaymentsModule>]
        public sealed class PaymentsHostModule;

        internal static class Program
        {
            private static Task Main(string[] args) => Task.CompletedTask;
        }
        """;

    private const string ARule = """
        [Invariant("Amount must not be negative")]
                internal bool AmountIsNotNegative() => Amount >= 0;
        """;

    [Fact]
    public void AVoidActionLoadingAnEntityWithARule_Compiles_AndChecksIt()
    {
        var (errors, module, _) = ModuleAndHost.Emit("Payments", [Module(ARule)], "Payments.Host", Host);

        errors.Should().BeEmpty("the generated invoker overrides a member its base declares");
        module.Should().NotBeEmpty();
    }

    /// <summary>The control: without a rule there is nothing to override, and it compiled before too.</summary>
    [Fact]
    public void WithoutARule_ItCompilesAsBefore()
    {
        var (errors, _) = ModuleAndHost.Generate("Payments", [Module("")], "Payments.Host", Host);

        errors.Should().BeEmpty();
    }
}
