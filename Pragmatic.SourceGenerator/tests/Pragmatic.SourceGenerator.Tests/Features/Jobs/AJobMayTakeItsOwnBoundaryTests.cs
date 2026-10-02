using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Mutation;
using Pragmatic.Composition.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Jobs;

/// <summary>
///     A job, or a service, may take its own module's boundary interface in its constructor,
///     and the build does not refuse it.
/// </summary>
/// <remarks>
///     <para>
///         The interface is emitted later in this same compilation, so while the constructor is read the
///         parameter is an error type with a bare name. A dependency check that compared that bare name
///         against the registered contracts would report <c>PRAG1641</c>, "not registered", for an
///         interface the host registers. The constructor path qualifies it through
///         <c>ServiceTypeDetector.QualifiedGeneratedService</c>, as the operations' fields do.
///     </para>
///     <para>
///         ⚠️ The control is the half that keeps this honest, as in
///         <c>TheOwnBoundaryInterfaceIsInjectableTests</c>: "trust any unresolved name" would pass the
///         first two and let a typo through.
///     </para>
/// </remarks>
public class AJobMayTakeItsOwnBoundaryTests
{
    private const string NotRegistered = "PRAG1641";

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromType<EndpointAttribute>(),
        GeneratorTestHelper.FromType<ServiceAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Jobs.IJob>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Mutation<>)),
        GeneratorTestHelper.FromType<global::Microsoft.Extensions.DependencyInjection.IServiceCollection>()
    ];

    private static SourceGenRunResult Run(string body)
    {
        var source = $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Actions.Mutation;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Jobs;
            using Pragmatic.Jobs.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Billing;

            [Module]
            public sealed class BillingModule;

            [Entity]
            public partial class Invoice : IEntity
            {
                public string Number { get; private set; } = "";
            }

            [Mutation(Mode = MutationMode.Create)]
            [Endpoint(HttpVerb.Post, "api/invoices")]
            public partial class CreateInvoiceMutation : Mutation<Invoice>
            {
                public required string Number { get; init; }
            }

            {{body}}
            """;

        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References);
    }

    private static string JobTaking(string type) => $$"""
        [Job]
        internal sealed partial class ChaseInvoicesJob({{type}} billing) : IJob
        {
            public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void AJobTakingTheInternalBoundaryInterface_IsNotReported()
        => IdsOf(Run(JobTaking("IBillingInternalActions"))).Should().NotContain(NotRegistered,
            "the host registers the boundary's internal interface; the name only fails to resolve because "
            + "this compilation has not emitted it yet");

    [Fact]
    public void AServiceTakingThePublicBoundaryInterface_IsNotReported()
    {
        var result = Run("""
            public interface IInvoiceReminders;

            [Service<IInvoiceReminders>]
            internal sealed class InvoiceReminders(IBillingActions billing) : IInvoiceReminders;
            """);

        IdsOf(result).Should().NotContain(NotRegistered);
    }

    /// <summary>The control: an unresolved name that no boundary here emits is still reported.</summary>
    [Fact]
    public void AJobTakingANameNoBoundaryEmits_IsStillReported()
        => IdsOf(Run(JobTaking("IPricingOracle"))).Should().Contain(NotRegistered,
            "nothing in this compilation emits that name, so nothing registers it");

    private static string[] IdsOf(SourceGenRunResult result)
        => [.. result.Diagnostics.Select(d => d.Id)];
}
