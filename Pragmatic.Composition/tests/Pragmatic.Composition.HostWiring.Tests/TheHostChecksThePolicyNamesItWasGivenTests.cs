// Pragmatic.Composition.HostWiring.Tests - The startup check of [ResiliencePolicy] names

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A host that wires resilience checks, once the pipeline is configured, that every
///     <c>[ResiliencePolicy]</c> name its modules declare is defined — and logs the ones that are not.
/// </summary>
/// <remarks>
///     Compiled for real, module and host: the module declares the names in its generated registration,
///     the host calls the check, and the check is only as good as the two agreeing on one type.
/// </remarks>
public sealed class TheHostChecksThePolicyNamesItWasGivenTests
{
    private const string Module = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Composition.Attributes;

        namespace Payments;

        [Boundary]
        public partial class PaymentsBoundary;

        [Module(Name = "Payments")]
        public sealed class PaymentsModule;
        """;

    private const string ActionWithAPolicy = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Resilience.Attributes;
        using Pragmatic.Result;

        namespace Payments.Charges;

        [DomainAction]
        [ResiliencePolicy("payments")]
        public partial class ChargeCard : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("ok"));
        }
        """;

    private const string ActionWithout = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Result;

        namespace Payments.Charges;

        [DomainAction]
        public partial class ChargeCard : DomainAction<string>
        {
            public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<string, IError>.Success("ok"));
        }
        """;

    private const string MutationWithAPolicy = """
        using System;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Resilience.Attributes;

        namespace Payments.Charges
        {
            [Entity]
            public partial class Charge : IEntity
            {
                public string Reference { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Create)]
            [ResiliencePolicy("payments")]
            public partial class RecordCharge : Mutation<Charge>
            {
                public string Reference { get; init; } = "";
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

    private const string TheCheck =
        "global::Pragmatic.Resilience.Configuration.UndefinedResiliencePolicies.Report(app.ApplicationServices);";

    [Fact]
    public void AModuleDeclaringAPolicy_GetsTheStartupCheck()
    {
        var (errors, generated) = ModuleAndHost.Generate("Payments", [Module, ActionWithAPolicy], "Payments.Host", Host);

        errors.Should().BeEmpty("the check names a type the host references — it has to compile");
        string.Join("\n", generated.Values).Should().Contain(TheCheck);
    }

    /// <summary>
    ///     A mutation's policy too: its invoker runs inside the pipeline and its registration declares the
    ///     name, and both have to compile against the whole reference set.
    /// </summary>
    [Fact]
    public void AModuleWhoseMutationDeclaresAPolicy_CompilesAndGetsTheStartupCheck()
    {
        var (errors, generated) = ModuleAndHost.Generate("Payments", [Module, MutationWithAPolicy], "Payments.Host", Host);

        errors.Should().BeEmpty("the invoker's override and the declaration are compiled into the module");
        string.Join("\n", generated.Values).Should().Contain(TheCheck);
    }

    /// <summary>The control: nobody declares a policy, the host wires no resilience and checks nothing.</summary>
    [Fact]
    public void NobodyDeclaringAPolicy_GetsNoCheck()
    {
        var (errors, generated) = ModuleAndHost.Generate("Payments", [Module, ActionWithout], "Payments.Host", Host);

        errors.Should().BeEmpty();
        string.Join("\n", generated.Values).Should().NotContain("UndefinedResiliencePolicies");
    }
}
